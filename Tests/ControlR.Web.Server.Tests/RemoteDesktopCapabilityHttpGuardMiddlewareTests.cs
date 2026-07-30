using System.Security.Claims;
using ControlR.Libraries.Shared.Constants;
using ControlR.Web.Client.Authz;
using ControlR.Web.Server.Middleware;
using ControlR.Web.Server.Options;
using ControlR.Web.Server.Services.Assistance;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Moq;

namespace ControlR.Web.Server.Tests;

public class RemoteDesktopCapabilityHttpGuardMiddlewareTests
{
  [Fact]
  public async Task Invoke_DuplicateCapabilityClaims_FailsClosed()
  {
    var nextCalled = false;
    var manager = new Mock<IAssistanceAuthorizationManager>(MockBehavior.Strict);
    var middleware = new RemoteDesktopCapabilityHttpGuardMiddleware(_ =>
    {
      nextCalled = true;
      return Task.CompletedTask;
    });
    var capability = LogonTokenCapability.RemoteDesktop.ToString();
    var context = CreateContext(capability, capability);

    await middleware.Invoke(context, manager.Object, CreateAdapterOptions().Object);

    Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    Assert.False(nextCalled);
  }

  [Fact]
  public async Task Invoke_NoCapabilityClaim_PreservesExistingPipeline()
  {
    var nextCalled = false;
    var manager = new Mock<IAssistanceAuthorizationManager>(MockBehavior.Strict);
    var middleware = new RemoteDesktopCapabilityHttpGuardMiddleware(_ =>
    {
      nextCalled = true;
      return Task.CompletedTask;
    });
    var context = CreateContext();

    await middleware.Invoke(context, manager.Object, CreateAdapterOptions().Object);

    Assert.True(nextCalled);
  }

  [Fact]
  public async Task Invoke_AdapterDisabled_PreservesStandardRemoteDesktopSession()
  {
    var nextCalled = false;
    var manager = new Mock<IAssistanceAuthorizationManager>(MockBehavior.Strict);
    var middleware = new RemoteDesktopCapabilityHttpGuardMiddleware(_ =>
    {
      nextCalled = true;
      return Task.CompletedTask;
    });
    var context = CreateContext(LogonTokenCapability.RemoteDesktop.ToString());

    await middleware.Invoke(
      context,
      manager.Object,
      CreateAdapterOptions(enabled: false).Object);

    Assert.True(nextCalled);
  }

  [Fact]
  public async Task Invoke_UnknownCapabilityClaim_FailsClosed()
  {
    var nextCalled = false;
    var manager = new Mock<IAssistanceAuthorizationManager>(MockBehavior.Strict);
    var middleware = new RemoteDesktopCapabilityHttpGuardMiddleware(_ =>
    {
      nextCalled = true;
      return Task.CompletedTask;
    });
    var context = CreateContext("UnknownCapability");

    await middleware.Invoke(context, manager.Object, CreateAdapterOptions().Object);

    Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    Assert.False(nextCalled);
  }

  [Fact]
  public async Task Invoke_ValidActiveCapability_AllowsViewerHub()
  {
    var nextCalled = false;
    var manager = CreateActiveManager();
    var middleware = new RemoteDesktopCapabilityHttpGuardMiddleware(_ =>
    {
      nextCalled = true;
      return Task.CompletedTask;
    });
    var context = CreateContext(LogonTokenCapability.RemoteDesktop.ToString());
    context.Request.Method = HttpMethods.Post;
    context.Request.Path = $"{AppConstants.ViewerHubPath}/negotiate";

    await middleware.Invoke(context, manager.Object, CreateAdapterOptions().Object);

    Assert.True(nextCalled);
  }

  [Fact]
  public async Task Invoke_ValidActiveCapability_AllowsWhitelistedRequest()
  {
    var nextCalled = false;
    var manager = CreateActiveManager();
    var middleware = new RemoteDesktopCapabilityHttpGuardMiddleware(_ =>
    {
      nextCalled = true;
      return Task.CompletedTask;
    });
    var context = CreateContext(LogonTokenCapability.RemoteDesktop.ToString());
    context.Request.Method = HttpMethods.Get;
    context.Request.Path = HttpConstants.Internal.PublicServerSettingsEndpoint;

    await middleware.Invoke(context, manager.Object, CreateAdapterOptions().Object);

    Assert.True(nextCalled);
  }

  [Fact]
  public async Task Invoke_ValidActiveCapability_DeniesAgentHub()
  {
    var nextCalled = false;
    var manager = CreateActiveManager();
    var middleware = new RemoteDesktopCapabilityHttpGuardMiddleware(_ =>
    {
      nextCalled = true;
      return Task.CompletedTask;
    });
    var context = CreateContext(LogonTokenCapability.RemoteDesktop.ToString());
    context.Request.Method = HttpMethods.Get;
    context.Request.Path = AppConstants.AgentHubPath;

    await middleware.Invoke(context, manager.Object, CreateAdapterOptions().Object);

    Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    Assert.False(nextCalled);
  }

  [Fact]
  public async Task Invoke_ValidActiveCapability_DeniesUnrelatedNonApiPost()
  {
    var nextCalled = false;
    var manager = CreateActiveManager();
    var middleware = new RemoteDesktopCapabilityHttpGuardMiddleware(_ =>
    {
      nextCalled = true;
      return Task.CompletedTask;
    });
    var context = CreateContext(LogonTokenCapability.RemoteDesktop.ToString());
    context.Request.Method = HttpMethods.Post;
    context.Request.Path = "/Account/Manage/DeletePersonalData";

    await middleware.Invoke(context, manager.Object, CreateAdapterOptions().Object);

    Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    Assert.False(nextCalled);
  }

  [Fact]
  public async Task Invoke_ValidInactiveCapability_FailsClosed()
  {
    var nextCalled = false;
    var manager = new Mock<IAssistanceAuthorizationManager>(MockBehavior.Strict);
    manager
      .Setup(x => x.IsActive(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync(false);
    var middleware = new RemoteDesktopCapabilityHttpGuardMiddleware(_ =>
    {
      nextCalled = true;
      return Task.CompletedTask;
    });
    var context = CreateContext(LogonTokenCapability.RemoteDesktop.ToString());

    await middleware.Invoke(context, manager.Object, CreateAdapterOptions().Object);

    Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    Assert.False(nextCalled);
  }

  private static Mock<IAssistanceAuthorizationManager> CreateActiveManager()
  {
    var manager = new Mock<IAssistanceAuthorizationManager>(MockBehavior.Strict);
    manager
      .Setup(x => x.IsActive(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
      .ReturnsAsync(true);
    return manager;
  }

  private static Mock<IOptionsMonitor<BootstrapOptions>> CreateAdapterOptions(
    bool enabled = true)
  {
    var options = new Mock<IOptionsMonitor<BootstrapOptions>>();
    options.SetupGet(x => x.CurrentValue).Returns(new BootstrapOptions
    {
      StableStationAdapterEnabled = enabled
    });
    return options;
  }

  private static DefaultHttpContext CreateContext(params string[] capabilities)
  {
    var context = new DefaultHttpContext();
    context.User = new ClaimsPrincipal(new ClaimsIdentity(
      capabilities.Select(value => new Claim(UserClaimTypes.SessionCapability, value)),
      "test"));
    return context;
  }
}
