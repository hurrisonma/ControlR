namespace ControlR.Web.Server.Services.Assistance;

public class AssistanceAuthorizationExpirationService(
  IServiceScopeFactory scopeFactory,
  TimeProvider timeProvider,
  ILogger<AssistanceAuthorizationExpirationService> logger) : BackgroundService
{
  private readonly ILogger<AssistanceAuthorizationExpirationService> _logger = logger;
  private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
  private readonly TimeProvider _timeProvider = timeProvider;

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    await ExpireStale(stoppingToken);
    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5), _timeProvider);
    while (await timer.WaitForNextTickAsync(stoppingToken))
    {
      await ExpireStale(stoppingToken);
    }
  }

  private async Task ExpireStale(CancellationToken cancellationToken)
  {
    try
    {
      await using var scope = _scopeFactory.CreateAsyncScope();
      var manager = scope.ServiceProvider.GetRequiredService<IAssistanceAuthorizationManager>();
      await manager.ExpireStale(cancellationToken);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      // Normal host shutdown.
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to expire stale remote assistance authorizations.");
    }
  }
}
