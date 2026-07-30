using ControlR.Web.Server.Authn;
using ControlR.Web.Server.Options;

namespace ControlR.Web.Server.Middleware;

public class StableStationServiceAccountHttpGuardMiddleware(RequestDelegate next)
{
  private readonly RequestDelegate _next = next;

  public async Task Invoke(
    HttpContext context,
    IOptionsMonitor<BootstrapOptions> bootstrapOptions)
  {
    var isStableStationServiceAccount = IsStableStationServiceAccount(
      context.User,
      bootstrapOptions.CurrentValue.ServerServiceAccountId);
    var path = context.Request.Path.Value ?? string.Empty;
    if (IsStableStationExclusivePath(path) && !isStableStationServiceAccount)
    {
      await WriteForbidden(context);
      return;
    }

    if (!isStableStationServiceAccount)
    {
      await _next(context);
      return;
    }

    if (IsAllowed(context.Request.Method, context.Request.Path.Value ?? string.Empty))
    {
      await _next(context);
      return;
    }

    await WriteForbidden(context);
  }

  internal static bool IsStableStationExclusivePath(string path)
  {
    var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
    return segments.Length >= 3 &&
      string.Equals(segments[0], "api", StringComparison.OrdinalIgnoreCase) &&
      string.Equals(segments[1], "v1", StringComparison.OrdinalIgnoreCase) &&
      (string.Equals(
         segments[2],
         "assistance-authorizations",
         StringComparison.OrdinalIgnoreCase) ||
       (segments.Length >= 4 &&
        string.Equals(segments[2], "stablestation", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(segments[3], "agent-enrollments", StringComparison.OrdinalIgnoreCase)));
  }

  internal static bool IsAllowed(string method, string path)
  {
    var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
    if (segments is ["api", "v1", "devices", var deviceId] &&
        HttpMethods.IsGet(method) &&
        Guid.TryParse(deviceId, out _))
    {
      return true;
    }

    if (segments is ["api", "v1", "assistance-authorizations"])
    {
      return HttpMethods.IsPost(method);
    }

    if (segments is ["api", "v1", "stablestation", "agent-enrollments"])
    {
      return HttpMethods.IsPost(method);
    }

    return segments is ["api", "v1", "assistance-authorizations", var authorizationId] &&
      Guid.TryParse(authorizationId, out _) &&
      (HttpMethods.IsGet(method) || HttpMethods.IsDelete(method));
  }

  private static bool IsStableStationServiceAccount(
    System.Security.Claims.ClaimsPrincipal user,
    Guid? serviceAccountId)
  {
    return serviceAccountId.HasValue &&
      serviceAccountId.Value != Guid.Empty &&
      Guid.TryParse(user.FindFirst(PrincipalClaimTypes.PrincipalId)?.Value, out var principalId) &&
      principalId == serviceAccountId.Value &&
      user.FindFirst(PrincipalClaimTypes.PrincipalType)?.Value == PrincipalClaimTypes.ServerServiceAccount;
  }

  private static async Task WriteForbidden(HttpContext context)
  {
    context.Response.StatusCode = StatusCodes.Status403Forbidden;
    await context.Response.WriteAsJsonAsync(new
    {
      error = "stablestation_service_account_forbidden"
    });
  }
}
