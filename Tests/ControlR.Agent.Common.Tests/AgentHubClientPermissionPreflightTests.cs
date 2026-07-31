using Bitbound.SimpleMessenger;
using ControlR.Agent.Common.Interfaces;
using ControlR.Agent.Common.Services;
using ControlR.Agent.Common.Services.FileManager;
using ControlR.Agent.Common.Services.Terminal;
using ControlR.Agent.Shared.Interfaces;
using ControlR.Agent.Shared.Services;
using ControlR.Libraries.Api.Contracts.Dtos.HubDtos;
using ControlR.Libraries.Api.Contracts.Dtos.IpcDtos;
using ControlR.Libraries.Api.Contracts.Dtos.RemoteControlDtos;
using ControlR.Libraries.Api.Contracts.Hubs;
using ControlR.Libraries.Ipc;
using ControlR.Libraries.Ipc.Interfaces;
using ControlR.Libraries.Shared.Services.FileSystem;
using ControlR.Libraries.Shared.Services.Processes;
using ControlR.Libraries.Shared.Services;
using ControlR.Libraries.Signalr.Client;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;

namespace ControlR.Agent.Common.Tests;

public class AgentHubClientPermissionPreflightTests
{
  [Fact]
  public async Task CreateTerminalSession_FailsBeforeCreatingProcess_WhenLocalGateIsDenied()
  {
    var gate = new Mock<IStableStationAssistanceGate>();
    var gateReason = "Remote assistance is not enabled by the local Connector.";
    gate
      .Setup(x => x.IsAllowed(
        It.IsAny<Guid?>(),
        It.IsAny<Guid?>(),
        It.IsAny<long?>(),
        out gateReason))
      .Returns(false);
    var terminalStore = new Mock<ITerminalStore>(MockBehavior.Strict);
    var sut = CreateSut(
      Mock.Of<IIpcServerStore>(),
      gate.Object,
      terminalStore.Object);

    var result = await sut.CreateTerminalSession(new TerminalSessionRequestDto(
      Guid.NewGuid(),
      "viewer-1"));

    Assert.False(result.IsSuccess);
    Assert.Equal(gateReason, result.Reason);
    terminalStore.Verify(
      x => x.CreateSession(It.IsAny<Guid>(), It.IsAny<string>()),
      Times.Never);
  }

  [Fact]
  public async Task CreateRemoteControlSession_FailsBeforeForwarding_WhenPreflightIsDenied()
  {
    var targetProcessId = 4242;
    var desktopClientRpc = new Mock<IDesktopClientRpcService>();
    desktopClientRpc
      .Setup(x => x.CheckOsPermissions(It.IsAny<CheckOsPermissionsIpcDto>()))
      .ReturnsAsync(new CheckOsPermissionsResponseIpcDto(false, "Remote control permission is missing."));

    var ipcServer = new Mock<IIpcServer>();
    ipcServer.SetupGet(x => x.Client).Returns(desktopClientRpc.Object);

    var serverStore = CreateIpcServerStore(targetProcessId, ipcServer.Object);
    var sut = CreateSut(serverStore.Object);

    var result = await sut.CreateRemoteControlSession(new RemoteControlSessionRequestDto(
      SessionId: Guid.NewGuid(),
      WebsocketUri: new Uri("wss://localhost:5001"),
      TargetSystemSession: 1,
      TargetProcessId: targetProcessId,
      DeviceId: Guid.NewGuid(),
      NotifyUserOnSessionStart: false,
      RequireConsent: false)
    {
      ViewerConnectionId = "viewer-1",
      ViewerName = "Test Viewer"
    });

    Assert.False(result.IsSuccess);
    Assert.Equal("Remote control permission is missing.", result.Reason);

    desktopClientRpc.Verify(
      x => x.CheckOsPermissions(It.Is<CheckOsPermissionsIpcDto>(dto =>
        dto.TargetProcessId == targetProcessId && dto.Scope == DesktopClientPermissionScope.RemoteControl)),
      Times.Once);
    desktopClientRpc.Verify(x => x.ReceiveRemoteControlRequest(It.IsAny<RemoteControlRequestIpcDto>()), Times.Never);
  }

  [Fact]
  public async Task RequestDesktopPreview_FailsBeforeForwarding_WhenPreflightIsDenied()
  {
    var targetProcessId = 4243;
    var desktopClientRpc = new Mock<IDesktopClientRpcService>();
    desktopClientRpc
      .Setup(x => x.CheckOsPermissions(It.IsAny<CheckOsPermissionsIpcDto>()))
      .ReturnsAsync(new CheckOsPermissionsResponseIpcDto(false, "Desktop preview permission is missing."));

    var ipcServer = new Mock<IIpcServer>();
    ipcServer.SetupGet(x => x.Client).Returns(desktopClientRpc.Object);

    var serverStore = CreateIpcServerStore(targetProcessId, ipcServer.Object);
    var sut = CreateSut(serverStore.Object);

    var result = await sut.RequestDesktopPreview(new DesktopPreviewRequestDto(
      RequesterId: Guid.NewGuid(),
      StreamId: Guid.NewGuid(),
      TargetProcessId: targetProcessId));

    Assert.False(result.IsSuccess);
    Assert.Equal("Desktop preview permission is missing.", result.Reason);

    desktopClientRpc.Verify(
      x => x.CheckOsPermissions(It.Is<CheckOsPermissionsIpcDto>(dto =>
        dto.TargetProcessId == targetProcessId && dto.Scope == DesktopClientPermissionScope.DesktopPreview)),
      Times.Once);
    desktopClientRpc.Verify(x => x.GetDesktopPreview(It.IsAny<DesktopPreviewRequestIpcDto>()), Times.Never);
  }

  private static Mock<IIpcServerStore> CreateIpcServerStore(int targetProcessId, IIpcServer ipcServer)
  {
    var process = new Mock<IProcess>();
    process.SetupGet(x => x.Id).Returns(targetProcessId);

    IpcServerRecord? serverRecord = new(process.Object, ipcServer);
    var serverStore = new Mock<IIpcServerStore>();
    serverStore
      .Setup(x => x.TryGetServer(targetProcessId, out serverRecord))
      .Returns(true);

    return serverStore;
  }

  private static AgentHubClient CreateSut(
    IIpcServerStore serverStore,
    IStableStationAssistanceGate? assistanceGate = null,
    ITerminalStore? terminalStore = null)
  {
    var systemEnvironment = new Mock<ISystemEnvironment>();
    systemEnvironment.SetupGet(x => x.IsDebug).Returns(true);
    var gate = new Mock<IStableStationAssistanceGate>();
    var gateReason = string.Empty;
    gate.Setup(x => x.IsAllowed(It.IsAny<RemoteControlSessionRequestDto>(), out gateReason))
      .Returns(true);

    return new AgentHubClient(
      Mock.Of<IHubConnection<IAgentHub>>(),
      systemEnvironment.Object,
      Mock.Of<IMessenger>(),
      terminalStore ?? Mock.Of<ITerminalStore>(),
      Mock.Of<IDesktopSessionProvider>(),
      serverStore,
      Mock.Of<IDesktopClientFileVerifier>(),
      Mock.Of<IHostApplicationLifetime>(),
      Mock.Of<IOptionsAccessor>(),
      Mock.Of<IProcessManager>(),
      Mock.Of<IPowerControl>(),
      Mock.Of<ILocalSocketProxy>(),
      Mock.Of<IFileManager>(),
      Mock.Of<IFileSystem>(),
      Mock.Of<IFileSystemPathProvider>(),
      Mock.Of<IDeviceInfoProvider>(),
      Mock.Of<IDesktopClientRepairCoordinator>(),
      Mock.Of<IAgentMaintenanceService>(),
      Mock.Of<IWakeOnLanService>(),
      Mock.Of<IAgentHeartbeatTimer>(),
      assistanceGate ?? gate.Object,
      Mock.Of<IRetryer>(),
      Mock.Of<ILogger<AgentHubClient>>());
  }
}
