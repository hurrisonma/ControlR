using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using ControlR.Agent.Common.Configuration;
using ControlR.Agent.Shared.Options;
using ControlR.Agent.Shared.Services;
using ControlR.Libraries.Api.Contracts.Constants;
using ControlR.Libraries.Api.Contracts.Dtos.ServerApi.Internal;
using ControlR.Libraries.Shared.Services.Encryption;
using Microsoft.Extensions.Options;

namespace ControlR.Agent.Common.Services;

internal interface IStableStationAgentProvisioner
{
  Task<StableStationAgentProvisioningResult> Provision(
    StableStationAgentProvisioningCommand command,
    ReadOnlyMemory<byte> sharedSecret,
    CancellationToken cancellationToken);
}

internal record StableStationAgentProvisioningResult(
  bool IsSuccess,
  string Error,
  Guid TenantId,
  Guid DeviceId);

internal interface IStableStationAgentEnrollmentClient
{
  Task<bool> CreateDevice(
    Uri serverUri,
    CreateDeviceRequestDto request,
    CancellationToken cancellationToken);
}

internal sealed class StableStationAgentEnrollmentClient(
  IHttpClientFactory httpClientFactory,
  ILogger<StableStationAgentEnrollmentClient> logger) : IStableStationAgentEnrollmentClient
{
  private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
  private readonly ILogger<StableStationAgentEnrollmentClient> _logger = logger;

  public async Task<bool> CreateDevice(
    Uri serverUri,
    CreateDeviceRequestDto request,
    CancellationToken cancellationToken)
  {
    try
    {
      using var client = _httpClientFactory.CreateClient();
      client.BaseAddress = serverUri;
      using var response = await client.PostAsJsonAsync(
        HttpConstants.Agent.DevicesEndpoint,
        request,
        cancellationToken);
      if (response.IsSuccessStatusCode)
      {
        return true;
      }
      _logger.LogWarning(
        "StableStation Agent enrollment endpoint returned HTTP {StatusCode}.",
        (int)response.StatusCode);
      return false;
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      throw;
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "StableStation Agent enrollment request failed.");
      return false;
    }
  }
}

internal class StableStationAgentProvisioner(
  TimeProvider timeProvider,
  IStableStationAgentEnrollmentClient enrollmentClient,
  IDeviceInfoProvider deviceInfoProvider,
  IEd25519KeyProvider keyProvider,
  IOptionsAccessor optionsAccessor,
  IOptionsMonitor<AgentAppOptions> appOptions,
  IOptions<StableStationAssistanceGateOptions> assistanceGateOptions,
  ILogger<StableStationAgentProvisioner> logger) : IStableStationAgentProvisioner
{
  private readonly IOptionsMonitor<AgentAppOptions> _appOptions = appOptions;
  private readonly StableStationAssistanceGateOptions _assistanceGateOptions = assistanceGateOptions.Value;
  private readonly IStableStationAgentEnrollmentClient _enrollmentClient = enrollmentClient;
  private readonly IDeviceInfoProvider _deviceInfoProvider = deviceInfoProvider;
  private readonly IEd25519KeyProvider _keyProvider = keyProvider;
  private readonly ILogger<StableStationAgentProvisioner> _logger = logger;
  private readonly object _nonceLock = new();
  private readonly IOptionsAccessor _optionsAccessor = optionsAccessor;
  private readonly SemaphoreSlim _provisionLock = new(1, 1);
  private readonly Dictionary<string, DateTimeOffset> _recentNonces = [];
  private readonly TimeProvider _timeProvider = timeProvider;

  public async Task<StableStationAgentProvisioningResult> Provision(
    StableStationAgentProvisioningCommand command,
    ReadOnlyMemory<byte> sharedSecret,
    CancellationToken cancellationToken)
  {
    var now = _timeProvider.GetUtcNow();
    if (!ValidateCommand(command, sharedSecret.Span, now, out var error))
    {
      return Failed(error);
    }

    lock (_nonceLock)
    {
      foreach (var expired in _recentNonces
                 .Where(x => x.Value <= now)
                 .Select(x => x.Key)
                 .ToArray())
      {
        _recentNonces.Remove(expired);
      }

      if (_recentNonces.ContainsKey(command.Nonce))
      {
        return Failed("Provisioning command nonce has already been used.");
      }
      _recentNonces[command.Nonce] = now.AddMinutes(2);
    }

    await _provisionLock.WaitAsync(cancellationToken);
    try
    {
      var options = _appOptions.CurrentValue;
      if (options.TenantId != Guid.Empty && options.TenantId != command.TenantId)
      {
        return Failed("Agent is already assigned to a different ControlR tenant.");
      }
      var serverUriChanged = options.ServerUri != command.ServerUri;
      options.ServerUri = command.ServerUri;
      if (options.TenantId == command.TenantId &&
          options.DeviceId != Guid.Empty &&
          !string.IsNullOrWhiteSpace(options.PrivateKey))
      {
        if (serverUriChanged)
        {
          await _optionsAccessor.UpdateAppOptions(options);
        }
        _logger.LogInformation(
          "StableStation Agent identity was already provisioned for endpoint {EndpointId} as device {DeviceId}.",
          command.EndpointId,
          options.DeviceId);
        return new StableStationAgentProvisioningResult(
          true,
          string.Empty,
          options.TenantId,
          options.DeviceId);
      }

      options.TenantId = command.TenantId;
      if (options.DeviceId == Guid.Empty)
      {
        options.DeviceId = Guid.NewGuid();
      }

      string publicKeyBase64;
      var generatedPrivateKey = false;
      if (string.IsNullOrWhiteSpace(options.PrivateKey))
      {
        var keyPair = _keyProvider.GenerateKeyPair();
        options.PrivateKey = Convert.ToBase64String(keyPair.PrivateKey);
        publicKeyBase64 = Convert.ToBase64String(keyPair.PublicKey);
        generatedPrivateKey = true;
      }
      else
      {
        publicKeyBase64 = _keyProvider.DerivePublicKeyBase64(options.PrivateKey);
      }

      await _optionsAccessor.UpdateAppOptions(options);
      var device = await _deviceInfoProvider.GetDeviceInfo();
      var request = new CreateDeviceRequestDto(
        device,
        command.InstallerKeyId,
        command.InstallerKeySecret,
        null,
        publicKeyBase64);
      var enrolled = await _enrollmentClient.CreateDevice(
        command.ServerUri,
        request,
        cancellationToken);
      if (!enrolled)
      {
        if (generatedPrivateKey)
        {
          options.PrivateKey = null;
          await _optionsAccessor.UpdateAppOptions(options);
        }
        _logger.LogWarning(
          "StableStation Agent provisioning failed for endpoint {EndpointId}. Reason: {Reason}",
          command.EndpointId,
          "ControlR rejected the request");
        return Failed("ControlR rejected the one-time Agent enrollment.");
      }

      _logger.LogInformation(
        "StableStation Agent provisioned for endpoint {EndpointId} as device {DeviceId}.",
        command.EndpointId,
        options.DeviceId);
      return new StableStationAgentProvisioningResult(
        true,
        string.Empty,
        options.TenantId,
        options.DeviceId);
    }
    finally
    {
      _provisionLock.Release();
    }
  }

  private static StableStationAgentProvisioningResult Failed(string error)
  {
    return new StableStationAgentProvisioningResult(
      false,
      error,
      Guid.Empty,
      Guid.Empty);
  }

  private bool ValidateCommand(
    StableStationAgentProvisioningCommand command,
    ReadOnlySpan<byte> sharedSecret,
    DateTimeOffset now,
    out string error)
  {
    if (!_assistanceGateOptions.Enabled ||
        !_assistanceGateOptions.ConnectorOwnsLifecycle ||
        string.IsNullOrWhiteSpace(command.EndpointId) ||
        command.EndpointId.Length > 160 ||
        command.EndpointId.Contains((char)10) ||
        command.TenantId == Guid.Empty ||
        command.InstallerKeyId == Guid.Empty ||
        string.IsNullOrWhiteSpace(command.InstallerKeySecret) ||
        command.InstallerKeySecret.Length is < 32 or > 512 ||
        command.InstallerKeySecret.Contains((char)10) ||
        command.ServerUri is null ||
        !command.ServerUri.IsAbsoluteUri ||
        command.ServerUri.Scheme != Uri.UriSchemeHttps ||
        string.IsNullOrWhiteSpace(command.ServerUri.Host) ||
        !string.IsNullOrEmpty(command.ServerUri.UserInfo) ||
        !string.IsNullOrEmpty(command.ServerUri.Query) ||
        !string.IsNullOrEmpty(command.ServerUri.Fragment) ||
        command.ServerUri.AbsolutePath != "/" ||
        !command.ServerUri.IsDefaultPort ||
        string.IsNullOrWhiteSpace(command.Nonce) ||
        command.Nonce.Length is < 16 or > 128 ||
        string.IsNullOrWhiteSpace(command.Signature) ||
        sharedSecret.Length < 32 ||
        command.IssuedAtUnixSeconds is < -62135596800 or > 253402300799)
    {
      error = "Invalid Agent provisioning command.";
      return false;
    }

    var issuedAt = DateTimeOffset.FromUnixTimeSeconds(command.IssuedAtUnixSeconds);
    if (issuedAt < now.AddSeconds(-45) ||
        issuedAt > now.AddSeconds(45))
    {
      error = "Invalid Agent provisioning command.";
      return false;
    }

    var canonical = string.Join((char)10,
      "provision",
      command.EndpointId,
      command.TenantId.ToString("D"),
      command.InstallerKeyId.ToString("D"),
      command.InstallerKeySecret,
      command.ServerUri.GetLeftPart(UriPartial.Authority).TrimEnd('/'),
      command.IssuedAtUnixSeconds.ToString(CultureInfo.InvariantCulture),
      command.Nonce);
    var expected = HMACSHA256.HashData(
      sharedSecret,
      Encoding.UTF8.GetBytes(canonical));
    byte[] actual;
    try
    {
      actual = Convert.FromHexString(command.Signature);
    }
    catch (FormatException)
    {
      error = "Agent provisioning signature is malformed.";
      return false;
    }

    if (!CryptographicOperations.FixedTimeEquals(expected, actual))
    {
      error = "Agent provisioning signature is invalid.";
      return false;
    }

    error = string.Empty;
    return true;
  }
}
