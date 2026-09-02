using System.Net;
using System.Runtime.InteropServices;
using System.Security.Claims;
using ControlR.Libraries.Api.Contracts.Dtos.Devices;
using ControlR.Libraries.Api.Contracts.Dtos.HubDtos;
using ControlR.Libraries.Api.Contracts.Dtos.ServerApi.V1.AssistanceAuthorizations;
using ControlR.Web.Client.Authz;
using ControlR.Web.Server.Primitives;
using ControlR.Web.Server.Services.Assistance;
using ControlR.Web.Server.Services.DeviceManagement;
using ControlR.Web.Server.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;

namespace ControlR.Web.Server.Tests;

public class AssistanceAuthorizationManagerTests(ITestOutputHelper testOutput)
{
  private readonly ITestOutputHelper _testOutput = testOutput;

  [Fact]
  public async Task CreateAndRevoke_ChangesCapabilityPrincipalFromActiveToInactive()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    using var scope = testApp.CreateScope();
    var services = scope.ServiceProvider;
    var tenant = await services.CreateTestTenant();
    var deviceId = Guid.NewGuid();
    await AddDevice(services, tenant.Id, deviceId, true);
    var manager = services.GetRequiredService<IAssistanceAuthorizationManager>();
    var connectorInstanceId = Guid.NewGuid();

    var result = await manager.Create(new CreateAssistanceAuthorizationRequestDto(
      deviceId,
      tenant.Id,
      "endpoint-1",
      "connector-session-1",
      connectorInstanceId,
      17,
      "operator-1",
      "Operator 1",
      "support-session-1",
      5), TestContext.Current.CancellationToken);

    Assert.True(result.IsSuccess, result.Reason);
    Assert.Equal(AssistanceAuthorizationStatus.Pending, result.Value.Authorization.Status);
    Assert.Equal(LogonTokenCapability.RemoteSupport, result.Value.Authorization.Capability);
    var principal = CapabilityPrincipal(
      result.Value.Authorization.AuthorizationId,
      connectorInstanceId,
      17,
      deviceId);
    Assert.True(await manager.IsActive(principal, TestContext.Current.CancellationToken));

    Assert.True(await manager.Revoke(
      result.Value.Authorization.AuthorizationId,
      "test_revocation",
      TestContext.Current.CancellationToken));
    Assert.False(await manager.IsActive(principal, TestContext.Current.CancellationToken));
  }

  [Fact]
  public async Task Create_WhenDeviceIsOffline_ReturnsConflictWithoutAuthorization()
  {
    await using var testApp = await TestAppBuilder.CreateTestApp(_testOutput);
    using var scope = testApp.CreateScope();
    var services = scope.ServiceProvider;
    var tenant = await services.CreateTestTenant();
    var deviceId = Guid.NewGuid();
    await AddDevice(services, tenant.Id, deviceId, false);
    var manager = services.GetRequiredService<IAssistanceAuthorizationManager>();

    var result = await manager.Create(new CreateAssistanceAuthorizationRequestDto(
      deviceId,
      tenant.Id,
      "endpoint-offline",
      "connector-session-offline",
      Guid.NewGuid(),
      1,
      "operator-offline",
      "Offline Operator",
      "support-session-offline",
      5), TestContext.Current.CancellationToken);

    Assert.False(result.IsSuccess);
    Assert.Equal(HttpResultErrorCode.Conflict, result.ErrorCode);
  }

  private static async Task AddDevice(
    IServiceProvider services,
    Guid tenantId,
    Guid deviceId,
    bool isOnline)
  {
    var manager = services.GetRequiredService<IDeviceManager>();
    await manager.AddOrUpdate(
      new DeviceUpdateRequestDto(
        Name: "Assistance Test Device",
        AgentVersion: "1.0.0",
        CpuUtilization: 0,
        Id: deviceId,
        Is64Bit: true,
        OsArchitecture: Architecture.X64,
        Platform: SystemPlatform.Windows,
        ProcessorCount: 4,
        OsDescription: "Windows",
        TenantId: tenantId,
        TotalMemory: 8192,
        TotalStorage: 256000,
        UsedMemory: 2048,
        UsedStorage: 64000,
        CurrentUsers: ["customer"],
        MacAddresses: ["00:11:22:33:44:55"],
        LocalIpV4: "10.0.0.2",
        LocalIpV6: "fe80::2",
        Drives: []),
      new DeviceConnectionContext(
        ConnectionId: "assistance-test-connection",
        RemoteIpAddress: IPAddress.Loopback,
        LastSeen: DateTimeOffset.UtcNow,
        IsOnline: isOnline));
  }

  private static ClaimsPrincipal CapabilityPrincipal(
    Guid authorizationId,
    Guid connectorInstanceId,
    long generation,
    Guid deviceId)
  {
    return new ClaimsPrincipal(new ClaimsIdentity(
    [
      new Claim(UserClaimTypes.AssistanceAuthorizationId, authorizationId.ToString()),
      new Claim(UserClaimTypes.AssistanceConnectorInstanceId, connectorInstanceId.ToString()),
      new Claim(UserClaimTypes.AssistanceEnableGeneration, generation.ToString()),
      new Claim(UserClaimTypes.DeviceSessionScope, deviceId.ToString()),
      new Claim(UserClaimTypes.SessionCapability, LogonTokenCapability.RemoteSupport.ToString())
    ], "test"));
  }
}
