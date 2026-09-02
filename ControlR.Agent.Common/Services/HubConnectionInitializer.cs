using System.Security.Cryptography;
using ControlR.Agent.Shared.Options;
using ControlR.Agent.Shared.Services;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ControlR.Agent.Common.Services;

internal class HubConnectionInitializer(
  TimeProvider timeProvider,
  IHubConnection<IAgentHub> hubConnection,
  IHostApplicationLifetime appLifetime,
  IOptionsMonitor<AgentAppOptions> appOptions,
  IAgentMaintenanceService agentUpdater,
  IAgentHeartbeatTimer agentHeartbeatTimer,
  ILogger<HubConnectionInitializer> logger) : BackgroundService
{
  private readonly IAgentHeartbeatTimer _agentHeartbeatTimer = agentHeartbeatTimer;
  private readonly IAgentMaintenanceService _agentUpdater = agentUpdater;
  private readonly IHostApplicationLifetime _appLifetime = appLifetime;
  private readonly IOptionsMonitor<AgentAppOptions> _appOptions = appOptions;
  private readonly IHubConnection<IAgentHub> _hubConnection = hubConnection;
  private readonly ILogger<HubConnectionInitializer> _logger = logger;
  private readonly TimeSpan _maxReconnectDelay = TimeSpan.FromSeconds(180);
  private readonly TimeSpan _maxReconnectJitter = TimeSpan.FromSeconds(20);
  private readonly TimeProvider _timeProvider = timeProvider;

  protected override async Task ExecuteAsync(CancellationToken cancellationToken)
  {
    var attempt = 1;
    while (!cancellationToken.IsCancellationRequested)
    {
      var serverUri = _appOptions.CurrentValue.ServerUri;
      if (serverUri is null)
      {
        await Task
          .Delay(TimeSpan.FromSeconds(1), _timeProvider, cancellationToken)
          .IgnoreOperationCanceledException();
        continue;
      }
      try
      {
        if (await Connect(serverUri, cancellationToken))
        {
          break;
        }
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error while initializing hub connection.");
      }

      try
      {
        var delay = GetNextRetryDelay(attempt);
        _logger.LogInformation("Waiting {delay} before next connection attempt.", delay);
        await Task
          .Delay(delay, _timeProvider, cancellationToken)
          .IgnoreOperationCanceledException();
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error while waiting before next connection attempt.");
      }
    }
  }

  public override async Task StopAsync(CancellationToken cancellationToken)
  {
    await base.StopAsync(cancellationToken);
    await _hubConnection.DisposeAsync();
  }

  private async Task<bool> Connect(
    Uri serverUri,
    CancellationToken cancellationToken)
  {
    var hubEndpoint = new Uri(serverUri, AppConstants.AgentHubPath);

    var result = await _hubConnection.Connect(
      hubEndpoint,
      true,
      options =>
      {
        options.SkipNegotiation = true;
        options.Transports = HttpTransportType.WebSockets;
      },
      cancellationToken);

    if (!result)
    {
      _logger.LogError("Failed to connect to hub.");
      return false;
    }

    await _agentHeartbeatTimer.SendDeviceHeartbeat();

    _hubConnection.Reconnected += HubConnection_Reconnected;
    _hubConnection.Reconnecting += HubConnection_Reconnecting;

    _logger.LogInformation("Connected to hub.");
    return true;
  }

  private TimeSpan GetNextRetryDelay(long retryCount)
  {
    var waitSeconds = Math.Min(Math.Pow(retryCount, 2), _maxReconnectDelay.TotalSeconds);
    var jitterMs = RandomNumberGenerator.GetInt32(0, (int)_maxReconnectJitter.TotalMilliseconds);
    var waitTime = TimeSpan.FromSeconds(waitSeconds) + TimeSpan.FromMilliseconds(jitterMs);
    return waitTime;
  }

  private async Task HubConnection_Reconnected(string? arg)
  {
    try
    {
      using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(30));
      using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
        cts.Token,
        _appLifetime.ApplicationStopping);

      await _agentHeartbeatTimer.SendDeviceHeartbeat();
      await _agentUpdater.CheckForUpdate(cancellationToken: linkedCts.Token);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error while handling hub reconnection.");
    }
  }

  private Task HubConnection_Reconnecting(Exception? arg)
  {
    _logger.LogInformation(arg, "Attempting to reconnect to hub.");
    return Task.CompletedTask;
  }
}
