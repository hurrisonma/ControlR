using ControlR.Libraries.Api.Contracts.Constants;
using ControlR.Web.Server.Options;
using ControlR.Web.Server.Services.Assistance;

namespace ControlR.Web.Server.Middleware;

public class RemoteDesktopCapabilityHttpGuardMiddleware(RequestDelegate next)
{
  private static readonly HashSet<string> _allowedExactGetPaths =
  [
    HttpConstants.Internal.EffectiveUserPreferencesEndpoint,
    HttpConstants.Internal.PublicServerSettingsEndpoint,
    HttpConstants.Internal.UserPreferencesEndpoint,
    HttpConstants.Internal.UserServerSettingsEndpoint,
    HttpConstants.Internal.VersionEndpoint,
    $"{HttpConstants.Internal.AuthEndpoint}/me"
  ];

  private readonly RequestDelegate _next = next;

  public async Task Invoke(
    HttpContext context,
    IAssistanceAuthorizationManager authorizationManager,
    IOptionsMonitor<BootstrapOptions> bootstrapOptions)
  {
    if (!bootstrapOptions.CurrentValue.StableStationAdapterEnabled)
    {
      await _next(context);
      return;
    }

    var capabilityClaims = context.User.FindAll(UserClaimTypes.SessionCapability).ToArray();
    if (capabilityClaims.Length == 0)
    {
      await _next(context);
      return;
    }

    if (capabilityClaims is not [var capability] ||
        capability.Value != LogonTokenCapability.RemoteDesktop.ToString())
    {
      context.Response.StatusCode = StatusCodes.Status403Forbidden;
      await context.Response.WriteAsJsonAsync(new { error = "invalid_session_capability" });
      return;
    }

    if (!await authorizationManager.IsActive(context.User, context.RequestAborted))
    {
      context.Response.StatusCode = StatusCodes.Status403Forbidden;
      await context.Response.WriteAsJsonAsync(new { error = "assistance_authorization_inactive" });
      return;
    }

    if (context.Request.Path.StartsWithSegments(AppConstants.AgentHubPath))
    {
      context.Response.StatusCode = StatusCodes.Status403Forbidden;
      await context.Response.WriteAsJsonAsync(new { error = "remote_desktop_capability_forbidden" });
      return;
    }

    if (context.Request.Path.StartsWithSegments(AppConstants.ViewerHubPath) ||
        context.Request.Path.StartsWithSegments(AppConstants.WebSocketRelayPath))
    {
      await _next(context);
      return;
    }

    if (!context.Request.Path.StartsWithSegments("/api"))
    {
      if (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
      {
        await _next(context);
        return;
      }

      context.Response.StatusCode = StatusCodes.Status403Forbidden;
      await context.Response.WriteAsJsonAsync(new { error = "remote_desktop_capability_forbidden" });
      return;
    }

    var path = context.Request.Path.Value ?? string.Empty;
    var isAllowedGet = HttpMethods.IsGet(context.Request.Method) &&
      (_allowedExactGetPaths.Contains(path) || IsScopedDeviceDetailsPath(path, context.User));
    if (isAllowedGet)
    {
      await _next(context);
      return;
    }

    context.Response.StatusCode = StatusCodes.Status403Forbidden;
    await context.Response.WriteAsJsonAsync(new
    {
      error = "remote_desktop_capability_forbidden"
    });
  }

  private static bool IsScopedDeviceDetailsPath(
    string path,
    System.Security.Claims.ClaimsPrincipal user)
  {
    var scopedDeviceId = user.FindFirst(UserClaimTypes.DeviceSessionScope)?.Value;
    return !string.IsNullOrWhiteSpace(scopedDeviceId) &&
      string.Equals(
        path,
        $"{HttpConstants.Internal.DevicesEndpoint}/{scopedDeviceId}",
        StringComparison.OrdinalIgnoreCase);
  }
}
