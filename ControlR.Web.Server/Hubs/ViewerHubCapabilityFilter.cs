using Microsoft.AspNetCore.SignalR;

namespace ControlR.Web.Server.Hubs;

public class ViewerHubCapabilityFilter(ILogger<ViewerHubCapabilityFilter> logger) : IHubFilter
{
  private readonly ILogger<ViewerHubCapabilityFilter> _logger = logger;

  public async ValueTask<object?> InvokeMethodAsync(
    HubInvocationContext invocationContext,
    Func<HubInvocationContext, ValueTask<object?>> next)
  {
    if (invocationContext.Hub is not ViewerHub ||
        ViewerHubCapabilityAuthorizer.IsHubMethodAllowed(
          invocationContext.Context.User,
          invocationContext.HubMethodName))
    {
      return await next(invocationContext);
    }

    _logger.LogWarning(
      "Viewer session capability denied hub method {HubMethodName} for user {UserIdentifier}.",
      invocationContext.HubMethodName,
      invocationContext.Context.UserIdentifier);

    throw new HubException("The authenticated session is not authorized for this operation.");
  }
}
