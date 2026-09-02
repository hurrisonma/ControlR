using ControlR.Web.Server.Services.Assistance;

namespace ControlR.Web.Server.Tests;

public class AssistanceViewerConnectionRegistryTests
{
  [Fact]
  public void TryRegister_AllowsOnlyOneViewerPerAuthorization()
  {
    var registry = new AssistanceViewerConnectionRegistry();
    var authorizationId = Guid.NewGuid();
    var firstAborted = false;

    Assert.True(registry.TryRegister(
      authorizationId,
      "viewer-1",
      () => firstAborted = true));
    Assert.False(registry.TryRegister(
      authorizationId,
      "viewer-2",
      () => { }));

    registry.Abort(authorizationId);
    Assert.True(firstAborted);
    Assert.True(registry.TryRegister(
      authorizationId,
      "viewer-3",
      () => { }));
  }

  [Fact]
  public void Unregister_StaleConnectionCannotRemoveCurrentViewer()
  {
    var registry = new AssistanceViewerConnectionRegistry();
    var authorizationId = Guid.NewGuid();
    Assert.True(registry.TryRegister(authorizationId, "viewer-current", () => { }));

    registry.Unregister(authorizationId, "viewer-stale");

    Assert.False(registry.TryRegister(authorizationId, "viewer-second", () => { }));
  }
}
