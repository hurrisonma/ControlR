using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using ControlR.Agent.Common.Configuration;
using ControlR.Libraries.Api.Contracts.Dtos.RemoteControlDtos;
using System.Globalization;
using Microsoft.Extensions.Options;

namespace ControlR.Agent.Common.Services;

public class StableStationAssistanceGate(
  TimeProvider timeProvider,
  IOptions<StableStationAssistanceGateOptions> options,
  ILogger<StableStationAssistanceGate> logger) : IStableStationAssistanceGate
{
  private readonly Channel<string> _closures = Channel.CreateUnbounded<string>();
  private readonly object _lock = new();
  private readonly ILogger<StableStationAssistanceGate> _logger = logger;
  private readonly StableStationAssistanceGateOptions _options = options.Value;
  private readonly Dictionary<string, DateTimeOffset> _recentNonces = [];
  private readonly TimeProvider _timeProvider = timeProvider;
  private Guid _connectorInstanceId;
  private long _enableGeneration;
  private DateTimeOffset _leaseExpiresAt;
  private bool _open;

  public ChannelReader<string> Closures => _closures.Reader;

  public bool Apply(
    StableStationAssistanceGateCommand command,
    ReadOnlySpan<byte> sharedSecret,
    out string reason)
  {
    reason = string.Empty;
    if (!_options.Enabled)
    {
      reason = "StableStation assistance gate is disabled.";
      return false;
    }

    var now = _timeProvider.GetUtcNow();
    if (!ValidateCommand(command, sharedSecret, now, out reason))
    {
      return false;
    }

    lock (_lock)
    {
      RemoveExpiredNonces(now);
      if (_recentNonces.ContainsKey(command.Nonce))
      {
        reason = "Gate command nonce has already been used.";
        return false;
      }
      _recentNonces[command.Nonce] = now.AddMinutes(2);

      if (command.Action is "enable" or "heartbeat")
      {
        if (command.Action == "heartbeat" &&
            (!_open || _connectorInstanceId != command.ConnectorInstanceId ||
             _enableGeneration != command.EnableGeneration))
        {
          reason = "Heartbeat does not match the active gate generation.";
          return false;
        }

        if (_open &&
            (_connectorInstanceId != command.ConnectorInstanceId ||
             _enableGeneration != command.EnableGeneration))
        {
          CloseLocked("generation_replaced");
        }

        _connectorInstanceId = command.ConnectorInstanceId;
        _enableGeneration = command.EnableGeneration;
        _leaseExpiresAt = DateTimeOffset.FromUnixTimeSeconds(command.LeaseExpiresAtUnixSeconds);
        _open = true;
        return true;
      }

      if (!_open ||
          _connectorInstanceId != command.ConnectorInstanceId ||
          _enableGeneration != command.EnableGeneration)
      {
        reason = "Disable does not match the active gate generation.";
        return false;
      }

      CloseLocked("connector_disabled");
      return true;
    }
  }

  public void ExpireIfRequired()
  {
    lock (_lock)
    {
      if (_open && _leaseExpiresAt <= _timeProvider.GetUtcNow())
      {
        CloseLocked("connector_heartbeat_expired");
      }
    }
  }

  public bool IsAllowed(RemoteControlSessionRequestDto request, out string reason)
  {
    return IsAllowed(
      request.AssistanceAuthorizationId,
      request.AssistanceConnectorInstanceId,
      request.AssistanceEnableGeneration,
      out reason);
  }

  public bool IsAllowed(
    Guid? assistanceAuthorizationId,
    Guid? assistanceConnectorInstanceId,
    long? assistanceEnableGeneration,
    out string reason)
  {
    if (!_options.Enabled)
    {
      reason = "StableStation assistance gate is not configured.";
      return false;
    }

    lock (_lock)
    {
      if (_open && _leaseExpiresAt <= _timeProvider.GetUtcNow())
      {
        CloseLocked("connector_heartbeat_expired");
      }

      if (!_open)
      {
        reason = "Remote assistance is not enabled by the local Connector.";
        return false;
      }

      if (!assistanceAuthorizationId.HasValue ||
          assistanceConnectorInstanceId != _connectorInstanceId ||
          assistanceEnableGeneration != _enableGeneration)
      {
        reason = "Remote assistance request does not match the active local generation.";
        return false;
      }

      reason = string.Empty;
      return true;
    }
  }

  private void CloseLocked(string reason)
  {
    if (!_open)
    {
      return;
    }
    _open = false;
    _connectorInstanceId = Guid.Empty;
    _enableGeneration = 0;
    _leaseExpiresAt = default;
    _closures.Writer.TryWrite(reason);
    _logger.LogWarning("StableStation assistance gate closed. Reason: {Reason}", reason);
  }

  private void RemoveExpiredNonces(DateTimeOffset now)
  {
    foreach (var nonce in _recentNonces.Where(x => x.Value <= now).Select(x => x.Key).ToArray())
    {
      _recentNonces.Remove(nonce);
    }
  }

  private bool ValidateCommand(
    StableStationAssistanceGateCommand command,
    ReadOnlySpan<byte> sharedSecret,
    DateTimeOffset now,
    out string reason)
  {
    if (command.Action is not ("enable" or "heartbeat" or "disable") ||
        string.IsNullOrWhiteSpace(command.EndpointId) ||
        command.EndpointId.Length > 160 ||
        (!string.IsNullOrWhiteSpace(_options.EndpointId) &&
         command.EndpointId != _options.EndpointId) ||
        command.ConnectorInstanceId == Guid.Empty ||
        command.EnableGeneration <= 0 ||
        command.Nonce.Length is < 16 or > 128 ||
        sharedSecret.Length < 32)
    {
      reason = "Invalid gate command identity.";
      return false;
    }

    var leaseExpiry = DateTimeOffset.FromUnixTimeSeconds(command.LeaseExpiresAtUnixSeconds);
    if (command.Action is "enable" or "heartbeat" &&
        (leaseExpiry <= now || leaseExpiry > now.AddSeconds(45)))
    {
      reason = "Gate lease is outside the accepted window.";
      return false;
    }

    var canonical = string.Join('\n',
      command.Action,
      command.EndpointId,
      command.ConnectorInstanceId.ToString("D"),
      command.EnableGeneration.ToString(CultureInfo.InvariantCulture),
      command.LeaseExpiresAtUnixSeconds.ToString(CultureInfo.InvariantCulture),
      command.Nonce);
    var expected = HMACSHA256.HashData(sharedSecret, Encoding.UTF8.GetBytes(canonical));
    byte[] actual;
    try
    {
      actual = Convert.FromHexString(command.Signature);
    }
    catch (FormatException)
    {
      reason = "Gate command signature is malformed.";
      return false;
    }

    if (!CryptographicOperations.FixedTimeEquals(expected, actual))
    {
      reason = "Gate command signature is invalid.";
      return false;
    }

    reason = string.Empty;
    return true;
  }
}
