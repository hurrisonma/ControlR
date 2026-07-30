using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ControlR.Agent.Common.Configuration;
using ControlR.Agent.Common.Services;
using ControlR.Agent.Shared.Interfaces;
using ControlR.Agent.Shared.Options;
using ControlR.Agent.Shared.Services;
using ControlR.ApiClient;
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
    var controlrApi = new Mock<IControlrApi>();
    var deviceInfoProvider = new Mock<IDeviceInfoProvider>();
    var keyProvider = new Mock<IEd25519KeyProvider>();
    var optionsAccessor = new Mock<IOptionsAccessor>();
    var provisioner = new StableStationAgentProvisioner(
      timeProvider,
      controlrApi.Object,
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
    controlrApi.VerifyNoOtherCalls();
    deviceInfoProvider.VerifyNoOtherCalls();
    keyProvider.VerifyNoOtherCalls();
    optionsAccessor.VerifyNoOtherCalls();
  }

  private static StableStationAgentProvisioningCommand CreateCommand(
    string endpointId,
    Guid tenantId,
    ReadOnlySpan<byte> secret,
    DateTimeOffset issuedAt)
  {
    var installerKeyId = Guid.NewGuid();
    var installerKeySecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    var issuedAtUnixSeconds = issuedAt.ToUnixTimeSeconds();
    var nonce = Guid.NewGuid().ToString("N");
    var canonical = string.Join('\n',
      "provision",
      endpointId,
      tenantId.ToString("D"),
      installerKeyId.ToString("D"),
      installerKeySecret,
      issuedAtUnixSeconds.ToString(CultureInfo.InvariantCulture),
      nonce);
    var signature = Convert.ToHexString(
      HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(canonical)));
    return new StableStationAgentProvisioningCommand(
      endpointId,
      tenantId,
      installerKeyId,
      installerKeySecret,
      issuedAtUnixSeconds,
      nonce,
      signature);
  }
}
