using System.Collections.ObjectModel;
using System.Runtime.Versioning;
using ControlR.Agent.Common.Configuration;
using ControlR.Agent.Common.Interfaces;
using ControlR.Agent.Common.Services;
using ControlR.Agent.Common.Services.Windows;
using ControlR.Agent.Shared.Services;
using ControlR.Libraries.Api.Contracts.Dtos.Devices;
using ControlR.Libraries.Api.Contracts.Dtos.IpcDtos;
using ControlR.Libraries.Ipc;
using ControlR.Libraries.Ipc.Interfaces;
using ControlR.Libraries.NativeInterop.Windows;
using ControlR.Libraries.Shared.Services;
using ControlR.Libraries.Shared.Primitives;
using ControlR.Libraries.Shared.Services.FileSystem;
using ControlR.Libraries.Shared.Services.Processes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace ControlR.Agent.Common.Tests;

[SupportedOSPlatform("windows8.0")]
public class DesktopClientWatcherWinTests
{
  private readonly Mock<IDesktopClientFileVerifier> _desktopClientFileVerifier = new();
  private readonly Mock<IDesktopSessionProvider> _desktopSessionProvider = new();
  private readonly Mock<ISystemEnvironment> _environment = new();
  private readonly Mock<IFileSystem> _fileSystem = new();
  private readonly Mock<IIpcServerStore> _ipcServerStore = new();
  private readonly ILogger<DesktopClientLaunchTracker> _launchTrackerLogger = new NullLogger<DesktopClientLaunchTracker>();
  private readonly Mock<ILogger<DesktopClientWatcherWin>> _logger = new();
  private readonly Mock<IFileSystemPathProvider> _pathProvider = new();
  private readonly Mock<IProcessManager> _processManager = new();
  private readonly Mock<IOptionsAccessor> _settingsProvider = new();
  private readonly FakeTimeProvider _timeProvider = new(DateTimeOffset.UtcNow);
  private readonly Mock<IWaiter> _waiter = new();
  private readonly Mock<IWin32Interop> _win32Interop = new();

  [Fact]
  public void Reconcile_WhenGraceWindowExpires_RemovesTrackedLaunch()
  {
    HashSet<int> activeSessionIds = [5];
    var tracker = new DesktopClientLaunchTracker(_timeProvider, _launchTrackerLogger);
    var process = CreateProcess(processId: 101, sessionId: 5, hasExited: false);

    tracker.TrackLaunch(5, process.Object);
    _timeProvider.Advance(DesktopClientLaunchTracker.StartupGracePeriod + TimeSpan.FromSeconds(1));

    tracker.Reconcile(activeSessionIds, []);

    process.Verify(x => x.Kill(), Times.Once);
    Assert.Equal(0, tracker.Count);
    Assert.False(tracker.IsSessionCovered(5, []));
  }

  [Fact]
  public void Reconcile_WhenTrackedProcessRegisters_ClearsTrackedLaunchWithoutStoppingProcess()
  {
    HashSet<int> activeSessionIds = [5];
    var tracker = new DesktopClientLaunchTracker(_timeProvider, _launchTrackerLogger);
    var process = CreateProcess(processId: 101, sessionId: 5, hasExited: false);

    tracker.TrackLaunch(5, process.Object);
    tracker.Reconcile(
      activeSessionIds,
      [new DesktopSession { ProcessId = 101, SystemSessionId = 5 }]);

    process.Verify(x => x.Kill(), Times.Never);
    Assert.Equal(0, tracker.Count);
  }

  [Fact]
  public void Reconcile_WhenDifferentProcessRegisters_StopsTrackedLaunch()
  {
    HashSet<int> activeSessionIds = [5];
    var tracker = new DesktopClientLaunchTracker(_timeProvider, _launchTrackerLogger);
    var process = CreateProcess(processId: 101, sessionId: 5, hasExited: false);

    tracker.TrackLaunch(5, process.Object);
    tracker.Reconcile(
      activeSessionIds,
      [new DesktopSession { ProcessId = 202, SystemSessionId = 5 }]);

    process.Verify(x => x.Kill(), Times.Once);
    Assert.Equal(0, tracker.Count);
  }

  [Fact]
  public void Reconcile_WhenTrackedProcessExits_RemovesTrackedLaunch()
  {
    HashSet<int> activeSessionIds = [5];
    var hasExited = false;
    var tracker = new DesktopClientLaunchTracker(_timeProvider, _launchTrackerLogger);
    var process = CreateProcess(processId: 101, sessionId: 5, hasExited: () => hasExited);

    tracker.TrackLaunch(5, process.Object);
    hasExited = true;

    tracker.Reconcile(activeSessionIds, []);

    process.Verify(x => x.Kill(), Times.Never);
    Assert.Equal(0, tracker.Count);
    Assert.False(tracker.IsSessionCovered(5, []));
  }

  [Fact]
  public void Reconcile_WhenTrackedProcessSessionIdThrows_RemovesTrackedLaunch()
  {
    HashSet<int> activeSessionIds = [5];
    var tracker = new DesktopClientLaunchTracker(_timeProvider, _launchTrackerLogger);
    var process = CreateProcessWithThrowingSessionId(processId: 101, hasExited: false);

    tracker.TrackLaunch(5, process.Object);

    tracker.Reconcile(activeSessionIds, []);

    process.Verify(x => x.Kill(), Times.Once);
    Assert.Equal(0, tracker.Count);
    Assert.False(tracker.IsSessionCovered(5, []));
  }

  [Fact]
  public async Task RunIteration_WhenInstallationVerificationSucceeds_ClearsInstallationFailureState()
  {
    IProcess? startedProcess = null;
    var repairCoordinator = new Mock<IDesktopClientRepairCoordinator>();
    var watcher = CreateWatcher(repairCoordinator: repairCoordinator.Object);

    _desktopSessionProvider
      .Setup(x => x.GetActiveDesktopClients())
      .ReturnsAsync([]);
    _win32Interop
      .Setup(x => x.CreateInteractiveSystemProcess(
        It.IsAny<string>(),
        5,
        true,
        out startedProcess))
      .Returns(false);

    await watcher.RunIteration(
      [new DesktopSession { SystemSessionId = 5 }],
      [],
      CancellationToken.None);

    repairCoordinator.Verify(x => x.ReportHealthy("desktop-installation"), Times.Once);
  }

  [Fact]
  public async Task RunIteration_WhenConfirmedDuplicateRejectsShutdown_RemovesAndStopsDuplicate()
  {
    var duplicateProcess = CreateProcess(processId: 101, sessionId: 5, hasExited: false);
    var duplicateClient = new Mock<IDesktopClientRpcService>();
    var duplicateServer = new Mock<IIpcServer>();
    var duplicateRecord = new IpcServerRecord(duplicateProcess.Object, duplicateServer.Object);
    var servers = new ReadOnlyDictionary<int, IpcServerRecord>(new Dictionary<int, IpcServerRecord>
    {
      [101] = duplicateRecord,
    });
    IpcServerRecord? removedRecord = duplicateRecord;
    var watcher = CreateWatcher();

    duplicateServer.SetupGet(x => x.Client).Returns(duplicateClient.Object);
    duplicateClient
      .Setup(x => x.ShutdownDesktopClient(It.IsAny<ShutdownCommandDto>()))
      .ThrowsAsync(new InvalidOperationException("IPC connection lost."));
    duplicateProcess
      .Setup(x => x.WaitForExitAsync(It.IsAny<CancellationToken>()))
      .ThrowsAsync(new OperationCanceledException());
    _ipcServerStore.SetupGet(x => x.Servers).Returns(servers);
    _ipcServerStore
      .Setup(x => x.TryRemove(101, out removedRecord))
      .Returns(true);

    await watcher.RunIteration(
      [new DesktopSession { SystemSessionId = 5 }],
      [new DesktopSession { ProcessId = 202, SystemSessionId = 5 }],
      CancellationToken.None);

    _ipcServerStore.Verify(x => x.TryRemove(101, out removedRecord), Times.Once);
    duplicateProcess.Verify(x => x.Kill(), Times.Once);
  }

  [Fact]
  public async Task RunIteration_WhenConfirmedDuplicateExitsGracefully_DoesNotForceStopProcess()
  {
    var hasExited = false;
    var duplicateProcess = CreateProcess(processId: 101, sessionId: 5, hasExited: () => hasExited);
    var duplicateClient = new Mock<IDesktopClientRpcService>();
    var duplicateServer = new Mock<IIpcServer>();
    var duplicateRecord = new IpcServerRecord(duplicateProcess.Object, duplicateServer.Object);
    var servers = new ReadOnlyDictionary<int, IpcServerRecord>(new Dictionary<int, IpcServerRecord>
    {
      [101] = duplicateRecord,
    });
    IpcServerRecord? removedRecord = duplicateRecord;
    var watcher = CreateWatcher();

    duplicateServer.SetupGet(x => x.Client).Returns(duplicateClient.Object);
    duplicateClient
      .Setup(x => x.ShutdownDesktopClient(It.IsAny<ShutdownCommandDto>()))
      .Callback(() => hasExited = true)
      .Returns(Task.CompletedTask);
    _ipcServerStore.SetupGet(x => x.Servers).Returns(servers);
    _ipcServerStore
      .Setup(x => x.TryRemove(101, out removedRecord))
      .Returns(true);

    await watcher.RunIteration(
      [new DesktopSession { SystemSessionId = 5 }],
      [new DesktopSession { ProcessId = 202, SystemSessionId = 5 }],
      CancellationToken.None);

    _ipcServerStore.Verify(x => x.TryRemove(101, out removedRecord), Times.Once);
    duplicateProcess.Verify(x => x.Kill(), Times.Never);
  }

  [Fact]
  public async Task RunIteration_WhenConnectorManagedSessionHasNoUser_DoesNotLaunchDesktopClient()
  {
    IProcess? startedProcess = null;
    var watcher = CreateWatcher(connectorOwnsLifecycle: true);

    await watcher.RunIteration(
      [new DesktopSession { SystemSessionId = 5, Username = string.Empty }],
      [],
      CancellationToken.None);

    _win32Interop.Verify(x => x.CreateInteractiveSystemProcess(
      It.IsAny<string>(),
      It.IsAny<int>(),
      It.IsAny<bool>(),
      out startedProcess), Times.Never);
  }

  [Fact]
  public async Task RunIteration_WhenConnectorManagedSessionIdIsInvalid_DoesNotLaunchDesktopClient()
  {
    IProcess? startedProcess = null;
    var watcher = CreateWatcher(connectorOwnsLifecycle: true);

    await watcher.RunIteration(
      [new DesktopSession { SystemSessionId = -1, Username = "test-user" }],
      [],
      CancellationToken.None);

    _win32Interop.Verify(x => x.CreateInteractiveSystemProcess(
      It.IsAny<string>(),
      It.IsAny<int>(),
      It.IsAny<bool>(),
      out startedProcess), Times.Never);
  }

  [Fact]
  public async Task RunIteration_WhenStandardControlRSessionHasNoUser_PreservesUpstreamLaunchBehavior()
  {
    IProcess? startedProcess = null;
    var watcher = CreateWatcher(connectorOwnsLifecycle: false);

    _desktopSessionProvider
      .Setup(x => x.GetActiveDesktopClients())
      .ReturnsAsync([]);
    _win32Interop
      .Setup(x => x.CreateInteractiveSystemProcess(
        It.IsAny<string>(),
        5,
        true,
        out startedProcess))
      .Returns(false);

    await watcher.RunIteration(
      [new DesktopSession { SystemSessionId = 5, Username = string.Empty }],
      [],
      CancellationToken.None);

    _win32Interop.Verify(x => x.CreateInteractiveSystemProcess(
      It.IsAny<string>(),
      5,
      true,
      out startedProcess), Times.Once);
  }

  [Fact]
  public async Task RunIteration_WhenConnectorManagedPendingLaunchTemporarilyHasNoUser_DoesNotStopProcess()
  {
    IProcess? startedProcess = null;
    var tracker = new DesktopClientLaunchTracker(_timeProvider, _launchTrackerLogger);
    var process = CreateProcess(processId: 101, sessionId: 5, hasExited: false);
    var watcher = CreateWatcher(connectorOwnsLifecycle: true, launchTracker: tracker);

    tracker.TrackLaunch(5, process.Object);

    await watcher.RunIteration(
      [new DesktopSession { SystemSessionId = 5, Username = string.Empty }],
      [],
      CancellationToken.None);

    _win32Interop.Verify(x => x.CreateInteractiveSystemProcess(
      It.IsAny<string>(),
      It.IsAny<int>(),
      It.IsAny<bool>(),
      out startedProcess), Times.Never);
    process.Verify(x => x.Kill(), Times.Never);
    Assert.Equal(1, tracker.Count);
  }

  [Fact]
  public async Task RunIteration_WhenRegisteredClientRemainsInIneligibleSession_StopsAfterGracePeriod()
  {
    var staleProcess = CreateProcess(processId: 101, sessionId: 5, hasExited: false);
    var staleClient = new Mock<IDesktopClientRpcService>();
    var staleServer = new Mock<IIpcServer>();
    var staleRecord = new IpcServerRecord(staleProcess.Object, staleServer.Object);
    var servers = new ReadOnlyDictionary<int, IpcServerRecord>(new Dictionary<int, IpcServerRecord>
    {
      [101] = staleRecord,
    });
    IpcServerRecord? removedRecord = staleRecord;
    var watcher = CreateWatcher(connectorOwnsLifecycle: true);
    var desktopClients = new[]
    {
      new DesktopSession { ProcessId = 101, SystemSessionId = 5 },
    };

    staleServer.SetupGet(x => x.Client).Returns(staleClient.Object);
    staleClient
      .Setup(x => x.ShutdownDesktopClient(It.IsAny<ShutdownCommandDto>()))
      .ThrowsAsync(new InvalidOperationException("IPC connection lost."));
    staleProcess
      .Setup(x => x.WaitForExitAsync(It.IsAny<CancellationToken>()))
      .ThrowsAsync(new OperationCanceledException());
    _ipcServerStore.SetupGet(x => x.Servers).Returns(servers);
    _ipcServerStore
      .Setup(x => x.TryRemove(101, out removedRecord))
      .Returns(true);

    await watcher.RunIteration([], desktopClients, CancellationToken.None);

    _ipcServerStore.Verify(x => x.TryRemove(101, out removedRecord), Times.Never);
    staleProcess.Verify(x => x.Kill(), Times.Never);

    _timeProvider.Advance(
      DesktopClientWatcherWin.IneligibleSessionGracePeriod + TimeSpan.FromSeconds(1));
    await watcher.RunIteration([], desktopClients, CancellationToken.None);

    _ipcServerStore.Verify(x => x.TryRemove(101, out removedRecord), Times.Once);
    staleProcess.Verify(x => x.Kill(), Times.Once);
  }

  [Fact]
  public async Task RunIteration_WhenSessionBecomesEligibleDuringGrace_DoesNotStopRegisteredClient()
  {
    var watcher = CreateWatcher(connectorOwnsLifecycle: true);
    var desktopClients = new[]
    {
      new DesktopSession { ProcessId = 101, SystemSessionId = 5 },
    };

    await watcher.RunIteration([], desktopClients, CancellationToken.None);
    _timeProvider.Advance(
      DesktopClientWatcherWin.IneligibleSessionGracePeriod + TimeSpan.FromSeconds(1));

    await watcher.RunIteration(
      [new DesktopSession { SystemSessionId = 5, Username = "test-user" }],
      desktopClients,
      CancellationToken.None);
    await watcher.RunIteration([], desktopClients, CancellationToken.None);

    _ipcServerStore.Verify(
      x => x.TryRemove(It.IsAny<int>(), out It.Ref<IpcServerRecord?>.IsAny),
      Times.Never);
  }

  [Fact]
  public async Task RunIteration_WhenPendingLaunchIsWithinGrace_DoesNotLaunchReplacement()
  {
    IProcess? startedProcess = null;
    var tracker = new DesktopClientLaunchTracker(_timeProvider, _launchTrackerLogger);
    var process = CreateProcess(processId: 101, sessionId: 5, hasExited: false);
    var watcher = CreateWatcher(launchTracker: tracker);

    tracker.TrackLaunch(5, process.Object);

    await watcher.RunIteration(
      [new DesktopSession { SystemSessionId = 5 }],
      [],
      CancellationToken.None);

    _win32Interop.Verify(x => x.CreateInteractiveSystemProcess(
      It.IsAny<string>(),
      It.IsAny<int>(),
      It.IsAny<bool>(),
      out startedProcess), Times.Never);
    process.Verify(x => x.Kill(), Times.Never);
  }

  [Fact]
  public async Task RunIteration_WhenInstanceIdIsMissing_OmitsInstanceIdArgument()
  {
    using var mutationLock = Mock.Of<IDisposable>();
    var launchedCommand = string.Empty;
    IProcess? startedProcess = CreateProcess(101, 5, hasExited: false).Object;
    var watcher = CreateWatcher(instanceId: string.Empty);

    _desktopSessionProvider
      .Setup(x => x.GetActiveDesktopClients())
      .ReturnsAsync([]);
    _win32Interop
      .Setup(x => x.CreateInteractiveSystemProcess(
        It.Is<string>(value => CaptureLaunchCommand(value, ref launchedCommand)),
        5,
        true,
        out startedProcess))
      .Returns(true);

    await watcher.RunIteration(
      [new DesktopSession { SystemSessionId = 5 }],
      [],
      CancellationToken.None);

    Assert.DoesNotContain("--instance-id", launchedCommand, StringComparison.Ordinal);
    Assert.Equal("\"C:\\ControlR\\DesktopClient\\ControlR.DesktopClient.exe\"", launchedCommand);
  }

  [Fact]
  public async Task RunIteration_WhenLaunchedProcessExitsBeforeRegistration_RemovesTrackedLaunchImmediately()
  {
    IProcess? startedProcess = CreateProcess(101, 5, hasExited: true).Object;
    var repairCoordinator = new Mock<IDesktopClientRepairCoordinator>();
    var tracker = new DesktopClientLaunchTracker(_timeProvider, _launchTrackerLogger);
    var watcher = CreateWatcher(repairCoordinator: repairCoordinator.Object, launchTracker: tracker);

    _desktopSessionProvider
      .Setup(x => x.GetActiveDesktopClients())
      .ReturnsAsync([]);
    _win32Interop
      .Setup(x => x.CreateInteractiveSystemProcess(
        It.IsAny<string>(),
        5,
        true,
        out startedProcess))
      .Returns(true);

    await watcher.RunIteration(
      [new DesktopSession { SystemSessionId = 5 }],
      [],
      CancellationToken.None);

    Assert.Equal(0, tracker.Count);
    repairCoordinator.Verify(
      x => x.ReportFailure(
        "windows-session-5",
        "Failed to launch desktop client in session 5.",
        false),
      Times.Once);
  }

  [Fact]
  public void TrackLaunch_MarksSessionCoveredBeforeRegistration()
  {
    var tracker = new DesktopClientLaunchTracker(_timeProvider, _launchTrackerLogger);
    var process = CreateProcess(processId: 101, sessionId: 5, hasExited: false);

    tracker.TrackLaunch(5, process.Object);

    Assert.True(tracker.IsSessionCovered(5, []));
  }

  [Fact]
  public void TrackLaunch_WhenReplacingExistingLaunch_StopsExistingProcess()
  {
    var tracker = new DesktopClientLaunchTracker(_timeProvider, _launchTrackerLogger);
    var existingProcess = CreateProcess(processId: 101, sessionId: 5, hasExited: false);
    var replacementProcess = CreateProcess(processId: 102, sessionId: 5, hasExited: false);

    tracker.TrackLaunch(5, existingProcess.Object);
    tracker.TrackLaunch(5, replacementProcess.Object);

    existingProcess.Verify(x => x.Kill(), Times.Once);
    Assert.Equal(1, tracker.Count);
    Assert.True(tracker.IsSessionCovered(5, []));
  }

  [Fact]
  public void Clear_StopsEveryPendingLaunch()
  {
    var tracker = new DesktopClientLaunchTracker(_timeProvider, _launchTrackerLogger);
    var process = CreateProcess(processId: 101, sessionId: 5, hasExited: false);

    tracker.TrackLaunch(5, process.Object);
    tracker.Clear();

    process.Verify(x => x.Kill(), Times.Once);
    Assert.Equal(0, tracker.Count);
  }

  private static bool CaptureLaunchCommand(string value, ref string launchedCommand)
  {
    launchedCommand = value;
    return true;
  }

  private static Mock<IProcess> CreateProcess(int processId, int sessionId, bool hasExited)
  {
    return CreateProcess(processId, sessionId, () => hasExited);
  }

  private static Mock<IProcess> CreateProcess(int processId, int sessionId, Func<bool> hasExited)
  {
    var process = new Mock<IProcess>();
    process.SetupGet(x => x.Id).Returns(processId);
    process.SetupGet(x => x.SessionId).Returns(sessionId);
    process.SetupGet(x => x.HasExited).Returns(() => hasExited());
    return process;
  }

  private static Mock<IProcess> CreateProcessWithThrowingSessionId(int processId, bool hasExited)
  {
    var process = new Mock<IProcess>();
    process.SetupGet(x => x.Id).Returns(processId);
    process.SetupGet(x => x.HasExited).Returns(hasExited);
    process.SetupGet(x => x.SessionId).Throws<InvalidOperationException>();
    return process;
  }

  private DesktopClientWatcherWin CreateWatcher(
    string instanceId = "instance-1",
    bool connectorOwnsLifecycle = false,
    IDesktopClientRepairCoordinator? repairCoordinator = null,
    IDesktopClientLaunchTracker? launchTracker = null)
  {
    launchTracker ??= new DesktopClientLaunchTracker(
      _timeProvider,
      _launchTrackerLogger);

    _environment.SetupGet(x => x.IsDebug).Returns(false);
    _environment.SetupGet(x => x.StartupDirectory).Returns("C:\\ControlR");
    _settingsProvider.SetupGet(x => x.InstanceId).Returns(instanceId);
    _desktopClientFileVerifier
      .Setup(x => x.VerifyFile(It.IsAny<string>()))
      .Returns(Result.Ok());
    _fileSystem
      .Setup(x => x.FileExists(It.IsAny<string>()))
      .Returns(true);
    _pathProvider.Setup(x => x.GetDesktopExecutablePath()).Returns("C:\\ControlR\\DesktopClient\\ControlR.DesktopClient.exe");
    _ipcServerStore
      .SetupGet(x => x.Servers)
      .Returns(new ReadOnlyDictionary<int, IpcServerRecord>(new Dictionary<int, IpcServerRecord>()));
    _waiter
      .Setup(x => x.WaitFor(
        It.IsAny<Func<bool>>(),
        It.IsAny<TimeSpan?>(),
        It.IsAny<Func<Task>?>(),
        It.IsAny<bool>(),
        It.IsAny<CancellationToken>()))
      .ReturnsAsync(false);

    return new DesktopClientWatcherWin(
      _timeProvider,
      _win32Interop.Object,
      _processManager.Object,
      _ipcServerStore.Object,
      repairCoordinator ?? Mock.Of<IDesktopClientRepairCoordinator>(),
      _environment.Object,
      _fileSystem.Object,
      _settingsProvider.Object,
      _desktopSessionProvider.Object,
      _desktopClientFileVerifier.Object,
      launchTracker,
      _waiter.Object,
      _pathProvider.Object,
      Options.Create(new StableStationAssistanceGateOptions
      {
        ConnectorOwnsLifecycle = connectorOwnsLifecycle,
      }),
      _logger.Object);
  }
}
