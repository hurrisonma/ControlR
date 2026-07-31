using ControlR.Libraries.Api.Contracts.Dtos.IpcDtos;
using Microsoft.Extensions.Hosting;

namespace ControlR.Agent.Common.Services;

public class StableStationAssistanceGateSessionTerminator(
  IStableStationAssistanceGate gate,
  IIpcServerStore ipcServerStore,
  ITerminalStore terminalStore,
  TimeProvider timeProvider,
  ILogger<StableStationAssistanceGateSessionTerminator> logger) : BackgroundService
{
  private readonly IStableStationAssistanceGate _gate = gate;
  private readonly IIpcServerStore _ipcServerStore = ipcServerStore;
  private readonly ILogger<StableStationAssistanceGateSessionTerminator> _logger = logger;
  private readonly ITerminalStore _terminalStore = terminalStore;
  private readonly TimeProvider _timeProvider = timeProvider;

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), _timeProvider);
    while (await timer.WaitForNextTickAsync(stoppingToken))
    {
      _gate.ExpireIfRequired();
      while (_gate.Closures.TryRead(out var reason))
      {
        _terminalStore.CloseAllSessions();
        await StopDesktopClients(reason);
      }
    }
  }

  private async Task StopDesktopClients(string reason)
  {
    foreach (var server in _ipcServerStore.Servers.Values)
    {
      try
      {
        await server.Server.Client.ShutdownDesktopClient(
          new ShutdownCommandDto($"StableStation assistance gate closed: {reason}"));
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "Failed to stop desktop client after assistance gate closure.");
      }
    }
  }
}
