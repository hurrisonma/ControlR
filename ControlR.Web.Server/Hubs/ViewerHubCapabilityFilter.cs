using ControlR.Web.Server.Services.Assistance;
using Microsoft.AspNetCore.SignalR;

namespace ControlR.Web.Server.Hubs;

public class ViewerHubCapabilityFilter(
  IAssistanceAuthorizationManager authorizationManager,
  AssistanceViewerConnectionRegistry connectionRegistry,
  ILogger<ViewerHubCapabilityFilter> logger) : IHubFilter
{
  private readonly IAssistanceAuthorizationManager _authorizationManager = authorizationManager;
  private readonly AssistanceViewerConnectionRegistry _connectionRegistry = connectionRegistry;
  private readonly ILogger<ViewerHubCapabilityFilter> _logger = logger;

  public async ValueTask<object?> InvokeMethodAsync(
    HubInvocationContext invocationContext,
    Func<HubInvocationContext, ValueTask<object?>> next)
  {
    if (invocationContext.Hub is not ViewerHub)
    {
      return await next(invocationContext);
    }

    var user = invocationContext.Context.User;
    if (!ViewerHubCapabilityAuthorizer.IsHubMethodAllowed(user, invocationContext.HubMethodName) ||
        IsAssistanceCapabilitySession(user) &&
        !await _authorizationManager.IsActive(user, invocationContext.Context.ConnectionAborted))
    {
      _logger.LogWarning(
        "Viewer session capability denied hub method {HubMethodName} for user {UserIdentifier}.",
        invocationContext.HubMethodName,
        invocationContext.Context.UserIdentifier);

      invocationContext.Context.Abort();
      throw new HubException("The authenticated session is not authorized for this operation.");
    }

    return await next(invocationContext);
  }

  public async Task OnConnectedAsync(
    HubLifetimeContext context,
    Func<HubLifetimeContext, Task> next)
  {
    var user = context.Context.User;
    if (!IsAssistanceCapabilitySession(user))
    {
      await next(context);
      return;
    }

    if (!TryGetAuthorizationId(user, out var authorizationId) ||
        !await _authorizationManager.IsActive(user, context.Context.ConnectionAborted))
    {
      context.Context.Abort();
      throw new HubException("The remote assistance authorization is no longer active.");
    }

    if (!_connectionRegistry.TryRegister(
        authorizationId,
        context.Context.ConnectionId,
        context.Context.Abort))
    {
      context.Context.Abort();
      throw new HubException("The remote assistance authorization is already in use.");
    }
    await _authorizationManager.MarkConnected(
      authorizationId,
      context.Context.ConnectionId,
      context.Context.ConnectionAborted);
    try
    {
      await next(context);
    }
    catch
    {
      _connectionRegistry.Unregister(
        authorizationId,
        context.Context.ConnectionId);
      await _authorizationManager.MarkEnded(
        authorizationId,
        "viewer_connection_failed",
        CancellationToken.None);
      throw;
    }
  }

  public async Task OnDisconnectedAsync(
    HubLifetimeContext context,
    Exception? exception,
    Func<HubLifetimeContext, Exception?, Task> next)
  {
    if (TryGetAuthorizationId(context.Context.User, out var authorizationId))
    {
      _connectionRegistry.Unregister(authorizationId, context.Context.ConnectionId);
      await _authorizationManager.MarkEnded(
        authorizationId,
        "viewer_disconnected",
        CancellationToken.None);
    }
    await next(context, exception);
  }

  private static bool IsAssistanceCapabilitySession(System.Security.Claims.ClaimsPrincipal? user)
  {
    var capabilities = user?.FindAll(UserClaimTypes.SessionCapability).ToArray() ?? [];
    return capabilities is [var capability] &&
      capability.Value is nameof(LogonTokenCapability.RemoteDesktop) or
        nameof(LogonTokenCapability.RemoteSupport);
  }

  private static bool TryGetAuthorizationId(
    System.Security.Claims.ClaimsPrincipal? user,
    out Guid authorizationId)
  {
    return Guid.TryParse(
      user?.FindFirst(UserClaimTypes.AssistanceAuthorizationId)?.Value,
      out authorizationId);
  }
}
