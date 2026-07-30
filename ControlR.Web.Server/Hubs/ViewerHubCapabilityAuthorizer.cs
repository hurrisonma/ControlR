using System.Security.Claims;

namespace ControlR.Web.Server.Hubs;

public static class ViewerHubCapabilityAuthorizer
{
  private static readonly HashSet<string> _remoteDesktopMethods =
  [
    nameof(ViewerHub.DisposeDeviceAccessActivity),
    nameof(ViewerHub.GetActiveDesktopSessions),
    nameof(ViewerHub.InvokeCtrlAltDel),
    nameof(ViewerHub.RequestRemoteControlPermission),
    nameof(ViewerHub.RequestRemoteControlSession),
    nameof(ViewerHub.StartDeviceAccessActivity),
  ];

  public static bool IsHubMethodAllowed(ClaimsPrincipal? user, string hubMethodName)
  {
    var capabilityClaims = user?
      .FindAll(UserClaimTypes.SessionCapability)
      .Select(x => x.Value)
      .ToArray() ?? [];

    if (capabilityClaims.Length == 0)
    {
      return true;
    }

    if (capabilityClaims is not [var capabilityValue] ||
        !Enum.TryParse<LogonTokenCapability>(capabilityValue, ignoreCase: false, out var capability))
    {
      return false;
    }

    return capability == LogonTokenCapability.RemoteDesktop &&
      _remoteDesktopMethods.Contains(hubMethodName);
  }
}
