using System.Diagnostics;
using System.Runtime.Versioning;
using ControlR.Agent.Common.Configuration;
using ControlR.Agent.Common.Interfaces;
using ControlR.Libraries.NativeInterop.Windows;
using ControlR.Libraries.Api.Contracts.Dtos.Devices;
using ControlR.Libraries.Shared.Helpers;
using Microsoft.Extensions.Hosting;
using ControlR.Libraries.Shared.Services.Processes;
using ControlR.Libraries.Shared.Services.FileSystem;
using ControlR.Libraries.Shared.Logging;
using ControlR.Agent.Common.Services.Windows.Internals;
using Microsoft.Extensions.Options;

namespace ControlR.Agent.Common.Services.Windows;

[SupportedOSPlatform("windows8.0")]
internal class DesktopClientWatcherWin(
  TimeProvider timeProvider,
  IWin32Interop win32Interop,
  IProcessManager processManager,
  IIpcServerStore ipcServerStore,
  IDesktopClientRepairCoordinator desktopClientRepairCoordinator,
  ISystemEnvironment environment,
  IFileSystem fileSystem,
  IOptionsAccessor optionsAccessor,
  IDesktopSessionProvider desktopSessionProvider,
  IDesktopClientFileVerifier desktopClientFileVerifier,
  IDesktopClientLaunchTracker launchTracker,
  IWaiter waiter,
  IFileSystemPathProvider pathProvider,
  IOptions<StableStationAssistanceGateOptions> assistanceGateOptions,
  ILogger<DesktopClientWatcherWin> logger) : BackgroundService
{
  private static readonly TimeSpan ClientExitTimeout = TimeSpan.FromSeconds(2);
  private static readonly TimeSpan ClientShutdownTimeout = TimeSpan.FromSeconds(2);
  internal static readonly TimeSpan IneligibleSessionGracePeriod = TimeSpan.FromSeconds(30);

  private readonly StableStationAssistanceGateOptions _assistanceGateOptions = assistanceGateOptions.Value;
  private readonly IDesktopClientFileVerifier _desktopClientFileVerifier = desktopClientFileVerifier;
  private readonly IDesktopClientRepairCoordinator _desktopClientRepairCoordinator = desktopClientRepairCoordinator;
  private readonly IDesktopSessionProvider _desktopSessionProvider = desktopSessionProvider;
  private readonly ISystemEnvironment _environment = environment;
  private readonly IFileSystem _fileSystem = fileSystem;
  private readonly IIpcServerStore _ipcServerStore = ipcServerStore;
  private readonly IDesktopClientLaunchTracker _launchTracker = launchTracker;
  private readonly ILogger<DesktopClientWatcherWin> _logger = logger;
  private readonly IOptionsAccessor _optionsAccessor = optionsAccessor;
  private readonly IFileSystemPathProvider _pathProvider = pathProvider;
  private readonly IProcessManager _processManager = processManager;
  private readonly TimeProvider _timeProvider = timeProvider;
  private readonly IWaiter _waiter = waiter;
  private readonly IWin32Interop _win32Interop = win32Interop;
  private readonly Dictionary<int, IneligibleDesktopClientState> _ineligibleDesktopClients = [];

  internal async Task RunIteration(
    IReadOnlyCollection<DesktopSession> activeSessions,
    DesktopSession[] desktopClients,
    CancellationToken stoppingToken)
  {
    var eligibleSessions = GetEligibleSessions(activeSessions);
    var reportedActiveSessionIds = activeSessions
      .Where(x => x.SystemSessionId >= 0)
      .Select(x => x.SystemSessionId)
      .ToHashSet();
    var eligibleSessionIds = eligibleSessions
      .Select(x => x.SystemSessionId)
      .ToHashSet();

    _launchTracker.Reconcile(reportedActiveSessionIds, desktopClients);
    await DisposeIneligibleClients(eligibleSessionIds, desktopClients, stoppingToken);

    var eligibleDesktopClients = desktopClients
      .Where(x => eligibleSessionIds.Contains(x.SystemSessionId))
      .ToArray();

    // Dispose of duplicate clients, those connected to the same session but not the "active" one.
    await DisposeDuplicateClients(eligibleDesktopClients, stoppingToken);

    foreach (var session in eligibleSessions)
    {
      var repairKey = GetRepairSessionKey(session.SystemSessionId);

      if (desktopClients.Any(x => x.SystemSessionId == session.SystemSessionId))
      {
        _desktopClientRepairCoordinator.ReportHealthy(repairKey);
        continue;
      }

      if (_launchTracker.IsSessionCovered(session.SystemSessionId, desktopClients))
      {
        continue;
      }

      _logger.LogInformation(
        "No desktop client found in session {SessionId}. Launching a new one.",
        session.SystemSessionId);

      var refreshedDesktopClients = await _desktopSessionProvider.GetActiveDesktopClients();
      _launchTracker.Reconcile(reportedActiveSessionIds, refreshedDesktopClients);

      if (refreshedDesktopClients.Any(x => x.SystemSessionId == session.SystemSessionId))
      {
        _desktopClientRepairCoordinator.ReportHealthy(repairKey);
        continue;
      }

      if (_launchTracker.IsSessionCovered(session.SystemSessionId, refreshedDesktopClients))
      {
        continue;
      }

      var installationVerificationResult = VerifyDesktopClientInstallation();
      if (!installationVerificationResult.IsSuccess)
      {
        _logger.LogErrorDeduped(
          "Desktop client launch skipped because the installed desktop client is invalid. Reason: {Reason}",
          args: installationVerificationResult.Reason);
        _desktopClientRepairCoordinator.ReportFailure(
          "desktop-installation",
          installationVerificationResult.Reason ?? "Desktop client installation is invalid.",
          immediate: true);
        continue;
      }

      _desktopClientRepairCoordinator.ReportHealthy("desktop-installation");

      var launchSucceeded = await LaunchDesktopClient(session.SystemSessionId, stoppingToken);
      if (!launchSucceeded)
      {
        _desktopClientRepairCoordinator.ReportFailure(
          repairKey,
          $"Failed to launch desktop client in session {session.SystemSessionId}.");
      }
    }
  }

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    if (_environment.IsDebug) 
    {
      _logger.LogInformation("Skipping DesktopClientWatcher in Debug mode.");
      return;
    }

    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5), _timeProvider);

    try
    {
      while (await timer.WaitForNextTick(false, stoppingToken))
      {
        try
        {
          var activeSessions = _win32Interop.GetActiveSessions();
          var desktopClients = await _desktopSessionProvider.GetActiveDesktopClients();
          await RunIteration(activeSessions, desktopClients, stoppingToken);
        }
        catch (OperationCanceledException)
        {
          _logger.LogInformation("Stopping DesktopClientWatcher.  Application is shutting down.");
          break;
        }
        catch (Exception ex)
        {
          _logger.LogErrorDeduped("Error while checking for desktop client processes.", exception: ex);
        }
      }
    }
    finally
    {
      _ineligibleDesktopClients.Clear();
      _launchTracker.Clear();
    }
  }

  private static string GetRepairSessionKey(int sessionId)
  {
    return $"windows-session-{sessionId}";
  }

  private DesktopSession[] GetEligibleSessions(IReadOnlyCollection<DesktopSession> activeSessions)
  {
    if (!_assistanceGateOptions.ConnectorOwnsLifecycle)
    {
      return [.. activeSessions];
    }

    return [.. activeSessions.Where(session =>
      session.SystemSessionId >= 0 &&
      !string.IsNullOrWhiteSpace(session.Username))];
  }

  private static bool TryGetProcessSessionId(IProcess process, out int sessionId)
  {
    try
    {
      sessionId = process.SessionId;
      return true;
    }
    catch
    {
      sessionId = -1;
      return false;
    }
  }

  private async Task DisposeDuplicateClients(DesktopSession[] activeClients, CancellationToken cancellationToken)
  {
    try
    {
      // Get all IPC servers grouped by session ID
      var serversBySession = new Dictionary<int, List<int>>();

      foreach (var (processId, serverRecord) in _ipcServerStore.Servers)
      {
        if (!TryGetProcessSessionId(serverRecord.Process, out var sessionId))
        {
          if (_ipcServerStore.TryRemove(processId, out var removedRecord) && removedRecord is not null)
          {
            Disposer.DisposeAll(removedRecord.Process, removedRecord.Server);
          }

          continue;
        }

        if (!serversBySession.TryGetValue(sessionId, out var serversInSession))
        {
          serversInSession = [];
          serversBySession[sessionId] = serversInSession;
        }

        serversInSession.Add(processId);
      }

      // For each session with active clients, find and dispose duplicates
      foreach (var activeClient in activeClients)
      {
        if (!serversBySession.TryGetValue(activeClient.SystemSessionId, out var serversInSession))
        {
          continue;
        }

        // Find duplicate servers (those with different PIDs than the active client)
        var duplicates = serversInSession
          .Where(processId => processId != activeClient.ProcessId)
          .ToList();

        if (duplicates.Count == 0)
        {
          continue;
        }

        _logger.LogWarning(
          "Found {DuplicateCount} duplicate desktop client(s) in session {SessionId}. " +
          "Active PID: {ActivePid}. Duplicate PIDs: {DuplicatePids}",
          duplicates.Count,
          activeClient.SystemSessionId,
          activeClient.ProcessId,
          string.Join(", ", duplicates));

        await Task.WhenAll(duplicates.Select(processId =>
          StopDesktopClient(
            activeClient.SystemSessionId,
            processId,
            "Duplicate client detected",
            "duplicate",
            cancellationToken)));
      }
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error while disposing duplicate clients.");
    }
  }

  private async Task DisposeIneligibleClients(
    IReadOnlySet<int> activeSessionIds,
    IReadOnlyCollection<DesktopSession> desktopClients,
    CancellationToken cancellationToken)
  {
    if (!_assistanceGateOptions.ConnectorOwnsLifecycle)
    {
      _ineligibleDesktopClients.Clear();
      return;
    }

    var currentProcessIds = desktopClients
      .Select(x => x.ProcessId)
      .ToHashSet();

    foreach (var processId in _ineligibleDesktopClients.Keys.ToArray())
    {
      if (!currentProcessIds.Contains(processId))
      {
        _ineligibleDesktopClients.Remove(processId);
      }
    }

    var now = _timeProvider.GetUtcNow();
    var clientsToStop = new List<DesktopSession>();

    foreach (var desktopClient in desktopClients)
    {
      if (activeSessionIds.Contains(desktopClient.SystemSessionId))
      {
        _ineligibleDesktopClients.Remove(desktopClient.ProcessId);
        continue;
      }

      if (!_ineligibleDesktopClients.TryGetValue(desktopClient.ProcessId, out var state) ||
          state.SessionId != desktopClient.SystemSessionId)
      {
        _ineligibleDesktopClients[desktopClient.ProcessId] = new IneligibleDesktopClientState(
          desktopClient.SystemSessionId,
          now);
        continue;
      }

      if (now - state.FirstObservedAt <= IneligibleSessionGracePeriod)
      {
        continue;
      }

      clientsToStop.Add(desktopClient);
      _ineligibleDesktopClients.Remove(desktopClient.ProcessId);
    }

    await Task.WhenAll(clientsToStop.Select(desktopClient =>
      StopDesktopClient(
        desktopClient.SystemSessionId,
        desktopClient.ProcessId,
        "No eligible logged-in Windows session remains",
        "ineligible",
        cancellationToken)));
  }

  private async Task<bool> LaunchDesktopClient(int sessionId, CancellationToken cancellationToken)
  {
    IProcess? trackedProcess = null;
    var trackedProcessId = -1;

    try
    {
      if (_environment.IsDebug)
      {
        await StartDebugSession();
        return true;
      }

      var binaryPath = _pathProvider.GetDesktopExecutablePath();
      var launchCommand = string.IsNullOrWhiteSpace(_optionsAccessor.InstanceId)
        ? $"\"{binaryPath}\""
        : $"\"{binaryPath}\" --instance-id {_optionsAccessor.InstanceId}";

      var result = _win32Interop.CreateInteractiveSystemProcess(
        launchCommand,
        sessionId,
        true,
        out var process);

      if (!result || process is null || process.Id == -1)
      {
        _logger.LogError("Failed to start desktop client process in session {SessionId}.", sessionId);
        return false;
      }

      trackedProcess = process;
      trackedProcessId = trackedProcess.Id;
      _launchTracker.TrackLaunch(sessionId, trackedProcess);

      _logger.LogInformation(
        "Launched desktop client process for session {SessionId}. PID: {ProcessId}",
        sessionId,
        trackedProcessId);

      using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
      using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cts.Token);

      var registeredQuickly = await _waiter.WaitFor(
        () => trackedProcess.HasExited || _ipcServerStore.ContainsServer(trackedProcessId),
        TimeSpan.FromMilliseconds(250),
        throwOnCancellation: false,
        cancellationToken: linkedCts.Token);

      if (registeredQuickly && _ipcServerStore.ContainsServer(trackedProcessId))
      {
        _logger.LogInformation(
          "Desktop client for session {SessionId} registered with IPC shortly after launch. PID: {ProcessId}",
          sessionId,
          trackedProcessId);
        return true;
      }

      if (trackedProcess.HasExited)
      {
        if (_launchTracker.TryRemove(sessionId, trackedProcessId, out var removedState) &&
            removedState is not null)
        {
          removedState.Dispose();
        }

        _logger.LogWarning(
          "Desktop client process for session {SessionId} exited before IPC registration completed. PID: {ProcessId}",
          sessionId,
          trackedProcessId);
        return false;
      }

      _logger.LogInformation(
        "Desktop client process for session {SessionId} is still starting. PID: {ProcessId}. Waiting for IPC registration in the background.",
        sessionId,
        trackedProcessId);

      return true;
    }
    catch (Exception ex)
    {
      if (trackedProcess is not null &&
          trackedProcessId >= 0 &&
          _launchTracker.TryRemove(sessionId, trackedProcessId, out var removedState) &&
          removedState is not null)
      {
        StopFailedLaunch(removedState);
      }

      _logger.LogErrorDeduped(
        "Error while launching desktop client in session {SessionId}. This error has been seen before.",
        args: sessionId,
        exception: ex);
      return false;
    }
  }

  private static bool IsProcessAlive(IProcess process)
  {
    try
    {
      return !process.HasExited;
    }
    catch
    {
      return false;
    }
  }

  private async Task StopDesktopClient(
    int sessionId,
    int processId,
    string shutdownReason,
    string lifecycleReason,
    CancellationToken cancellationToken)
  {
    if (!_ipcServerStore.TryRemove(processId, out var clientRecord))
    {
      return;
    }

    try
    {
      _logger.LogInformation(
        "Stopping {LifecycleReason} desktop client. Session: {SessionId}, PID: {ProcessId}",
        lifecycleReason,
        sessionId,
        processId);

      try
      {
        var shutdownDto = new ShutdownCommandDto(shutdownReason);
        await clientRecord.Server.Client
          .ShutdownDesktopClient(shutdownDto)
          .WaitAsync(ClientShutdownTimeout, cancellationToken);
      }
      catch (Exception ex)
      {
        _logger.LogWarning(
          ex,
          "{LifecycleReason} desktop client did not accept the shutdown request. Session: {SessionId}, PID: {ProcessId}",
          lifecycleReason,
          sessionId,
          processId);
      }

      if (IsProcessAlive(clientRecord.Process))
      {
        try
        {
          using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
          cts.CancelAfter(ClientExitTimeout);
          await clientRecord.Process.WaitForExitAsync(cts.Token);
        }
        catch (Exception ex)
        {
          _logger.LogDebug(
            ex,
            "{LifecycleReason} desktop client did not exit during the graceful window. Session: {SessionId}, PID: {ProcessId}",
            lifecycleReason,
            sessionId,
            processId);
        }
      }

      if (IsProcessAlive(clientRecord.Process))
      {
        clientRecord.Process.Kill();
        _logger.LogInformation(
          "Terminated {LifecycleReason} desktop client. Session: {SessionId}, PID: {ProcessId}",
          lifecycleReason,
          sessionId,
          processId);
      }
    }
    catch (Exception ex)
    {
      _logger.LogWarning(
        ex,
        "Failed to terminate {LifecycleReason} desktop client. Session: {SessionId}, PID: {ProcessId}",
        lifecycleReason,
        sessionId,
        processId);
    }
    finally
    {
      Disposer.DisposeAll(clientRecord.Process, clientRecord.Server);
    }
  }

  private sealed record IneligibleDesktopClientState(
    int SessionId,
    DateTimeOffset FirstObservedAt);

  private void StopFailedLaunch(DesktopClientLaunchState launchState)
  {
    try
    {
      if (IsProcessAlive(launchState.Process))
      {
        launchState.Process.Kill();
      }
    }
    catch (Exception ex)
    {
      _logger.LogWarning(
        ex,
        "Failed to stop desktop client after its launch workflow failed. Session: {SessionId}, PID: {ProcessId}",
        launchState.SessionId,
        launchState.ProcessId);
    }
    finally
    {
      launchState.Dispose();
    }
  }

  private async Task StartDebugSession()
  {
    var solutionDirResult = IoHelper.GetSolutionDir(Environment.CurrentDirectory);

    if (!solutionDirResult.IsSuccess)
    {
      _logger.LogErrorDeduped(
        "Failed to find solution directory. Desktop client cannot be launched in debug mode. Reason: {Reason}",
        args: solutionDirResult.Reason);
      return;
    }

    var desktopClientBin = Path.Combine(
      solutionDirResult.Value,
      "ControlR.DesktopClient",
      "bin",
      "Debug");

    var desktopClientPath = _fileSystem
      .GetFiles(desktopClientBin, AppConstants.DesktopClientFileName, SearchOption.AllDirectories)
      .OrderByDescending(x => new FileInfo(x).CreationTime)
      .FirstOrDefault();

    if (string.IsNullOrWhiteSpace(desktopClientPath))
    {
      throw new FileNotFoundException("DesktopClient binary not found.", desktopClientPath);
    }

    var psi = new ProcessStartInfo
    {
      WorkingDirectory = Path.GetDirectoryName(desktopClientPath),
      UseShellExecute = true,
      FileName = desktopClientPath,
      Arguments = "--instance-id localhost"
    };

    var process = _processManager.Start(psi);
    Guard.IsNotNull(process);

    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    var waitResult = await _waiter.WaitFor(
      () => !process.HasExited && _ipcServerStore.ContainsServer(process.Id),
      TimeSpan.FromSeconds(1),
      throwOnCancellation: false,
      cancellationToken: cts.Token);

    if (!waitResult)
    {
      _logger.LogErrorDeduped(
        "Launched desktop client process in debug mode but it failed to register with IPC within the expected time. PID: {ProcessId}",
        args: process.Id);
    }
  }

  private Result VerifyDesktopClientInstallation()
  {
    var desktopExecutablePath = _pathProvider.GetDesktopExecutablePath();
    if (!_fileSystem.FileExists(desktopExecutablePath))
    {
      return Result.Fail($"Desktop client executable was not found at '{desktopExecutablePath}'.");
    }

    return _desktopClientFileVerifier.VerifyFile(desktopExecutablePath);
  }
}
