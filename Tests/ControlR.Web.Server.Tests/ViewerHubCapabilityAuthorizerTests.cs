using System.Security.Claims;
using ControlR.Web.Client.Authz;
using ControlR.Web.Server.Hubs;

namespace ControlR.Web.Server.Tests;

public class ViewerHubCapabilityAuthorizerTests
{
  [Fact]
  public void InvalidCapability_DeniesRemoteDesktopMethod()
  {
    var user = CreateUser("InvalidCapability");

    var isAllowed = ViewerHubCapabilityAuthorizer.IsHubMethodAllowed(
      user,
      nameof(ViewerHub.RequestRemoteControlSession));

    Assert.False(isAllowed);
  }

  [Fact]
  public void MultipleCapabilities_DenyRemoteDesktopMethod()
  {
    var identity = new ClaimsIdentity(
    [
      new Claim(UserClaimTypes.SessionCapability, LogonTokenCapability.RemoteDesktop.ToString()),
      new Claim(UserClaimTypes.SessionCapability, LogonTokenCapability.RemoteDesktop.ToString()),
    ]);
    var user = new ClaimsPrincipal(identity);

    var isAllowed = ViewerHubCapabilityAuthorizer.IsHubMethodAllowed(
      user,
      nameof(ViewerHub.RequestRemoteControlSession));

    Assert.False(isAllowed);
  }

  [Theory]
  [InlineData(nameof(ViewerHub.DisposeDeviceAccessActivity))]
  [InlineData(nameof(ViewerHub.GetActiveDesktopSessions))]
  [InlineData(nameof(ViewerHub.InvokeCtrlAltDel))]
  [InlineData(nameof(ViewerHub.RequestRemoteControlPermission))]
  [InlineData(nameof(ViewerHub.RequestRemoteControlSession))]
  [InlineData(nameof(ViewerHub.StartDeviceAccessActivity))]
  public void RemoteDesktopCapability_AllowsRemoteDesktopMethods(string hubMethodName)
  {
    var user = CreateUser(LogonTokenCapability.RemoteDesktop.ToString());

    var isAllowed = ViewerHubCapabilityAuthorizer.IsHubMethodAllowed(user, hubMethodName);

    Assert.True(isAllowed);
  }

  [Theory]
  [InlineData(nameof(ViewerHub.CreateTerminalSession))]
  [InlineData(nameof(ViewerHub.RequestVncSession))]
  [InlineData(nameof(ViewerHub.SendAgentUpdateTrigger))]
  [InlineData(nameof(ViewerHub.SendChatMessage))]
  [InlineData(nameof(ViewerHub.SendPowerStateChange))]
  [InlineData(nameof(ViewerHub.SendTerminalInput))]
  [InlineData(nameof(ViewerHub.UninstallAgent))]
  [InlineData(nameof(ViewerHub.UploadFile))]
  [InlineData("FutureUnmappedMethod")]
  public void RemoteDesktopCapability_DeniesOtherAndUnknownMethods(string hubMethodName)
  {
    var user = CreateUser(LogonTokenCapability.RemoteDesktop.ToString());

    var isAllowed = ViewerHubCapabilityAuthorizer.IsHubMethodAllowed(user, hubMethodName);

    Assert.False(isAllowed);
  }

  [Fact]
  public void UnrestrictedSession_PreservesExistingAccess()
  {
    var user = new ClaimsPrincipal(new ClaimsIdentity());

    var isAllowed = ViewerHubCapabilityAuthorizer.IsHubMethodAllowed(
      user,
      nameof(ViewerHub.CreateTerminalSession));

    Assert.True(isAllowed);
  }

  private static ClaimsPrincipal CreateUser(string capability)
  {
    var identity = new ClaimsIdentity(
      [new Claim(UserClaimTypes.SessionCapability, capability)]);
    return new ClaimsPrincipal(identity);
  }
}
