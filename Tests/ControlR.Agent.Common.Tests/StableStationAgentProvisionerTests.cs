using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ControlR.Agent.Common.Configuration;
using ControlR.Agent.Common.Services;
using ControlR.Agent.Shared.Interfaces;
using ControlR.Agent.Shared.Options;
using ControlR.Agent.Shared.Services;
using ControlR.Libraries.Shared.Services.Encryption;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace ControlR.Agent.Common.Tests;

public class StableStationAgentProvisionerTests
{
  [Fact]
  public async Task Provision_ExistingIdentity_ReturnsIdentityWithoutCreatingAnotherDevice()
  {
    var now = DateTimeOffset.UtcNow;
    var timeProvider = new FakeTimeProvider(now);
    var tenantId = Guid.NewGuid();
    var deviceId = Guid.NewGuid();
    var endpointId = "endpoint-test-1";
    var secret = RandomNumberGenerator.GetBytes(32);
    var appOptions = new AgentAppOptions
    {
      TenantId = tenantId,
      DeviceId = deviceId,
      PrivateKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
      ServerUri = new Uri("https://assist.example.test")
    };
    var appOptionsMonitor = new Mock<IOptionsMonitor<AgentAppOptions>>();
    appOptionsMonitor.SetupGet(x => x.CurrentValue).Returns(appOptions);
    var enrollmentClient = new Mock<IStableStationAgentEnrollmentClient>();
    var deviceInfoProvider = new Mock<IDeviceInfoProvider>();
    var keyProvider = new Mock<IEd25519KeyProvider>();
    var optionsAccessor = new Mock<IOptionsAccessor>();
    var provisioner = new StableStationAgentProvisioner(
      timeProvider,
      enrollmentClient.Object,
      deviceInfoProvider.Object,
      keyProvider.Object,
      optionsAccessor.Object,
      appOptionsMonitor.Object,
      Options.Create(new StableStationAssistanceGateOptions
      {
        Enabled = true,
        ConnectorOwnsLifecycle = true
      }),
      NullLogger<StableStationAgentProvisioner>.Instance);
    var command = CreateCommand(endpointId, tenantId, secret, now);

    var result = await provisioner.Provision(command, secret, CancellationToken.None);

    Assert.True(result.IsSuccess, result.Error);
    Assert.Equal(tenantId, result.TenantId);
    Assert.Equal(deviceId, result.DeviceId);
    enrollmentClient.VerifyNoOtherCalls();
    deviceInfoProvider.VerifyNoOtherCalls();
    keyProvider.VerifyNoOtherCalls();
    optionsAccessor.VerifyNoOtherCalls();
  }

  [Fact]
  public async Task Provision_ExistingIdentityWithNewServerUri_PersistsServerUri()
  {
    var now = DateTimeOffset.UtcNow;
    var timeProvider = new FakeTimeProvider(now);
    var tenantId = Guid.NewGuid();
    var deviceId = Guid.NewGuid();
    var endpointId = "endpoint-test-1";
    var secret = RandomNumberGenerator.GetBytes(32);
    var appOptions = new AgentAppOptions
    {
      TenantId = tenantId,
      DeviceId = deviceId,
      PrivateKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
      ServerUri = new Uri("https://assist.old.example.test")
    };
    var appOptionsMonitor = new Mock<IOptionsMonitor<AgentAppOptions>>();
    appOptionsMonitor.SetupGet(x => x.CurrentValue).Returns(appOptions);
    var enrollmentClient = new Mock<IStableStationAgentEnrollmentClient>();
    var deviceInfoProvider = new Mock<IDeviceInfoProvider>();
    var keyProvider = new Mock<IEd25519KeyProvider>();
    var optionsAccessor = new Mock<IOptionsAccessor>();
    var provisioner = new StableStationAgentProvisioner(
      timeProvider,
      enrollmentClient.Object,
      deviceInfoProvider.Object,
      keyProvider.Object,
      optionsAccessor.Object,
      appOptionsMonitor.Object,
      Options.Create(new StableStationAssistanceGateOptions
      {
        Enabled = true,
        ConnectorOwnsLifecycle = true
      }),
      NullLogger<StableStationAgentProvisioner>.Instance);
    var newServerUri = new Uri("https://assist.new.example.test");
    var command = CreateCommand(endpointId, tenantId, secret, now, newServerUri);

    var result = await provisioner.Provision(command, secret, CancellationToken.None);

    Assert.True(result.IsSuccess, result.Error);
    Assert.Equal(newServerUri, appOptions.ServerUri);
    optionsAccessor.Verify(
      x => x.UpdateAppOptions(appOptions),
      Times.Once);
    enrollmentClient.VerifyNoOtherCalls();
    deviceInfoProvider.VerifyNoOtherCalls();
    keyProvider.VerifyNoOtherCalls();
  }

  [Fact]
  public async Task Provision_NonHttpsServerUri_IsRejected()
  {
    var now = DateTimeOffset.UtcNow;
    var timeProvider = new FakeTimeProvider(now);
    var tenantId = Guid.NewGuid();
    var endpointId = "endpoint-test-1";
    var secret = RandomNumberGenerator.GetBytes(32);
    var appOptions = new AgentAppOptions();
    var appOptionsMonitor = new Mock<IOptionsMonitor<AgentAppOptions>>();
    appOptionsMonitor.SetupGet(x => x.CurrentValue).Returns(appOptions);
    var enrollmentClient = new Mock<IStableStationAgentEnrollmentClient>();
    var deviceInfoProvider = new Mock<IDeviceInfoProvider>();
    var keyProvider = new Mock<IEd25519KeyProvider>();
    var optionsAccessor = new Mock<IOptionsAccessor>();
    var provisioner = new StableStationAgentProvisioner(
      timeProvider,
      enrollmentClient.Object,
      deviceInfoProvider.Object,
      keyProvider.Object,
      optionsAccessor.Object,
      appOptionsMonitor.Object,
      Options.Create(new StableStationAssistanceGateOptions
      {
        Enabled = true,
        ConnectorOwnsLifecycle = true
      }),
      NullLogger<StableStationAgentProvisioner>.Instance);
    var command = CreateCommand(
      endpointId,
      tenantId,
      secret,
      now,
      new Uri("http://assist.example.test"));

    var result = await provisioner.Provision(command, secret, CancellationToken.None);

    Assert.False(result.IsSuccess);
    Assert.Equal("Invalid Agent provisioning command.", result.Error);
    enrollmentClient.VerifyNoOtherCalls();
    deviceInfoProvider.VerifyNoOtherCalls();
    keyProvider.VerifyNoOtherCalls();
    optionsAccessor.VerifyNoOtherCalls();
  }

  private static StableStationAgentProvisioningCommand CreateCommand(
    string endpointId,
    Guid tenantId,
    ReadOnlySpan<byte> secret,
    DateTimeOffset issuedAt,
    Uri? serverUri = null)
  {
    var installerKeyId = Guid.NewGuid();
    var installerKeySecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    serverUri ??= new Uri("https://assist.example.test");
    var issuedAtUnixSeconds = issuedAt.ToUnixTimeSeconds();
    var nonce = Guid.NewGuid().ToString("N");
    var canonical = string.Join('\n',
      "provision",
      endpointId,
      tenantId.ToString("D"),
      installerKeyId.ToString("D"),
      installerKeySecret,
      serverUri.GetLeftPart(UriPartial.Authority),
      issuedAtUnixSeconds.ToString(CultureInfo.InvariantCulture),
      nonce);
    var signature = Convert.ToHexString(
      HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(canonical)));
    return new StableStationAgentProvisioningCommand(
      endpointId,
      tenantId,
      installerKeyId,
      installerKeySecret,
      serverUri,
      issuedAtUnixSeconds,
      nonce,
      signature);
  }
}
