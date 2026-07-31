using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ControlR.Agent.Common.Configuration;
using ControlR.Agent.Common.Services;
using ControlR.Libraries.Api.Contracts.Dtos.RemoteControlDtos;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace ControlR.Agent.Common.Tests;

public class StableStationAssistanceGateTests
{
  private const string _endpointId = "endpoint-test-1";
  private readonly Guid _authorizationId = Guid.NewGuid();
  private readonly Guid _connectorInstanceId = Guid.NewGuid();
  private readonly byte[] _secret = RandomNumberGenerator.GetBytes(32);
  private readonly FakeTimeProvider _timeProvider = new(DateTimeOffset.UtcNow);

  [Fact]
  public void Apply_Disable_ClosesExactGenerationImmediately()
  {
    var gate = CreateGate();
    var enable = CreateCommand("enable", 15, _timeProvider.GetUtcNow().AddSeconds(40));
    Assert.True(gate.Apply(enable, _secret, out var enableReason), enableReason);
    var disable = CreateCommand("disable", 15, DateTimeOffset.UnixEpoch);

    Assert.True(gate.Apply(disable, _secret, out var disableReason), disableReason);
    Assert.False(gate.IsAllowed(CreateRequest(15), out _));
    Assert.True(gate.Closures.TryRead(out var closeReason));
    Assert.Equal("connector_disabled", closeReason);
  }

  [Fact]
  public void Apply_Enable_AllowsOnlyExactAssistanceGeneration()
  {
    var gate = CreateGate();
    var command = CreateCommand("enable", 7, _timeProvider.GetUtcNow().AddSeconds(40));

    Assert.True(gate.Apply(command, _secret, out var enableReason), enableReason);
    Assert.True(gate.IsAllowed(CreateRequest(7), out var allowedReason), allowedReason);
    Assert.False(gate.IsAllowed(CreateRequest(8), out var deniedReason));
    Assert.Contains("does not match", deniedReason);
  }

  [Fact]
  public void Apply_Enable_AllowsTerminalIdentityOnlyForExactAssistanceGeneration()
  {
    var gate = CreateGate();
    var command = CreateCommand("enable", 19, _timeProvider.GetUtcNow().AddSeconds(40));

    Assert.True(gate.Apply(command, _secret, out var enableReason), enableReason);
    Assert.True(gate.IsAllowed(
      _authorizationId,
      _connectorInstanceId,
      19,
      out var allowedReason), allowedReason);
    Assert.False(gate.IsAllowed(
      _authorizationId,
      _connectorInstanceId,
      20,
      out var deniedReason));
    Assert.Contains("does not match", deniedReason);
  }

  [Fact]
  public void Apply_Enable_WithConnectorManagedEndpoint_AcceptsSignedEndpoint()
  {
    var gate = new StableStationAssistanceGate(
      _timeProvider,
      Options.Create(new StableStationAssistanceGateOptions
      {
        Enabled = true,
        EndpointId = string.Empty,
        Port = 48173,
        SharedSecretFile = "unused-by-unit-test"
      }),
      NullLogger<StableStationAssistanceGate>.Instance);
    var command = CreateCommand("enable", 8, _timeProvider.GetUtcNow().AddSeconds(40));

    Assert.True(gate.Apply(command, _secret, out var reason), reason);
    Assert.True(gate.IsAllowed(CreateRequest(8), out var allowedReason), allowedReason);
  }

  [Fact]
  public void Apply_InvalidSignature_IsRejectedWithoutOpeningGate()
  {
    var gate = CreateGate();
    var command = CreateCommand("enable", 13, _timeProvider.GetUtcNow().AddSeconds(40)) with
    {
      Signature = Convert.ToHexString(RandomNumberGenerator.GetBytes(32))
    };

    Assert.False(gate.Apply(command, _secret, out var applyReason));
    Assert.Contains("signature is invalid", applyReason);
    Assert.False(gate.IsAllowed(CreateRequest(13), out _));
  }

  [Fact]
  public void Apply_ReplayedCommand_IsRejected()
  {
    var gate = CreateGate();
    var command = CreateCommand("enable", 9, _timeProvider.GetUtcNow().AddSeconds(40));

    Assert.True(gate.Apply(command, _secret, out var firstReason), firstReason);
    Assert.False(gate.Apply(command, _secret, out var replayReason));
    Assert.Contains("already been used", replayReason);
  }

  [Fact]
  public void ExpireIfRequired_ClosesGateAndRejectsExistingGeneration()
  {
    var gate = CreateGate();
    var command = CreateCommand("enable", 11, _timeProvider.GetUtcNow().AddSeconds(40));
    Assert.True(gate.Apply(command, _secret, out var enableReason), enableReason);

    _timeProvider.Advance(TimeSpan.FromSeconds(41));
    gate.ExpireIfRequired();

    Assert.False(gate.IsAllowed(CreateRequest(11), out var reason));
    Assert.Contains("not enabled", reason);
    Assert.True(gate.Closures.TryRead(out var closeReason));
    Assert.Equal("connector_heartbeat_expired", closeReason);
  }

  [Fact]
  public void IsAllowed_DisabledGate_FailsClosed()
  {
    var gate = new StableStationAssistanceGate(
      _timeProvider,
      Options.Create(new StableStationAssistanceGateOptions()),
      NullLogger<StableStationAssistanceGate>.Instance);

    Assert.False(gate.IsAllowed(CreateRequest(1), out var reason));
    Assert.Contains("not configured", reason);
  }

  private StableStationAssistanceGateCommand CreateCommand(
    string action,
    long generation,
    DateTimeOffset leaseExpiresAt)
  {
    var nonce = Guid.NewGuid().ToString("N");
    var leaseExpiresAtUnixSeconds = leaseExpiresAt.ToUnixTimeSeconds();
    var canonical = string.Join('\n',
      action,
      _endpointId,
      _connectorInstanceId.ToString("D"),
      generation.ToString(CultureInfo.InvariantCulture),
      leaseExpiresAtUnixSeconds.ToString(CultureInfo.InvariantCulture),
      nonce);
    var signature = Convert.ToHexString(
      HMACSHA256.HashData(_secret, Encoding.UTF8.GetBytes(canonical)));
    return new StableStationAssistanceGateCommand(
      action,
      _endpointId,
      _connectorInstanceId,
      generation,
      leaseExpiresAtUnixSeconds,
      nonce,
      signature);
  }

  private StableStationAssistanceGate CreateGate()
  {
    return new StableStationAssistanceGate(
      _timeProvider,
      Options.Create(new StableStationAssistanceGateOptions
      {
        Enabled = true,
        EndpointId = _endpointId,
        Port = 48173,
        SharedSecretFile = "unused-by-unit-test"
      }),
      NullLogger<StableStationAssistanceGate>.Instance);
  }

  private RemoteControlSessionRequestDto CreateRequest(long generation)
  {
    return new RemoteControlSessionRequestDto(
      Guid.NewGuid(),
      new Uri("wss://assist.example.test/relay"),
      1,
      42,
      Guid.NewGuid(),
      false,
      false)
    {
      AssistanceAuthorizationId = _authorizationId,
      AssistanceConnectorInstanceId = _connectorInstanceId,
      AssistanceEnableGeneration = generation
    };
  }
}
