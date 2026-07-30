using System.Security.Claims;
using ControlR.Web.Server.Authn;
using ControlR.Web.Server.Middleware;
using ControlR.Web.Server.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;

namespace ControlR.Web.Server.Tests;

public class StableStationServiceAccountHttpGuardMiddlewareTests
{
  [Theory]
  [InlineData("/api/v1/assistance-authorizations")]
  [InlineData("/api/v1/assistance-authorizations/4d124e1d-652f-4b45-9187-f0dd7cb1ea49")]
  [InlineData("/api/v1/stablestation/agent-enrollments")]
  [InlineData("/API/V1/StableStation/Agent-Enrollments")]
  public void IsStableStationExclusivePath_AssistanceRoutes_ReturnsTrue(string path)
  {
    Assert.True(StableStationServiceAccountHttpGuardMiddleware.IsStableStationExclusivePath(path));
  }

  [Theory]
  [InlineData("/api/v1/devices/4d124e1d-652f-4b45-9187-f0dd7cb1ea49")]
  [InlineData("/api/v1/installer-keys")]
  [InlineData("/api/v1/logon-tokens/external")]
  public void IsStableStationExclusivePath_GeneralRoutes_ReturnsFalse(string path)
  {
    Assert.False(StableStationServiceAccountHttpGuardMiddleware.IsStableStationExclusivePath(path));
  }

  [Fact]
  public async Task Invoke_OtherServerServiceAccountOnExclusiveRoute_ReturnsForbidden()
  {
    var stableStationServiceAccountId = Guid.NewGuid();
    var otherServiceAccountId = Guid.NewGuid();
    var nextCalled = false;
    var middleware = new StableStationServiceAccountHttpGuardMiddleware(_ =>
    {
      nextCalled = true;
      return Task.CompletedTask;
    });
    var context = new DefaultHttpContext();
    using var services = new ServiceCollection().BuildServiceProvider();
    context.RequestServices = services;
    context.Request.Method = HttpMethods.Post;
    context.Request.Path = "/api/v1/stablestation/agent-enrollments";
    context.User = new ClaimsPrincipal(new ClaimsIdentity([
      new Claim(PrincipalClaimTypes.PrincipalId, otherServiceAccountId.ToString()),
      new Claim(PrincipalClaimTypes.PrincipalType, PrincipalClaimTypes.ServerServiceAccount)
    ], "test"));
    var options = new Mock<IOptionsMonitor<BootstrapOptions>>();
    options.SetupGet(x => x.CurrentValue).Returns(new BootstrapOptions
    {
      ServerServiceAccountId = stableStationServiceAccountId
    });

    await middleware.Invoke(context, options.Object);

    Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    Assert.False(nextCalled);
  }

  [Theory]
  [InlineData("GET", "/api/v1/devices")]
  [InlineData("DELETE", "/api/v1/devices/4d124e1d-652f-4b45-9187-f0dd7cb1ea49")]
  [InlineData("POST", "/api/v1/logon-tokens/external")]
  [InlineData("GET", "/api/v1/service-accounts")]
  [InlineData("GET", "/api/v1/tenants")]
  [InlineData("GET", "/api/v1/assistance-authorizations/not-a-guid")]
  public void IsAllowed_NonAdapterRoutes_ReturnsFalse(string method, string path)
  {
    Assert.False(StableStationServiceAccountHttpGuardMiddleware.IsAllowed(method, path));
  }

  [Theory]
  [InlineData("GET", "/api/v1/devices/4d124e1d-652f-4b45-9187-f0dd7cb1ea49")]
  [InlineData("POST", "/api/v1/assistance-authorizations")]
  [InlineData("POST", "/api/v1/stablestation/agent-enrollments")]
  [InlineData("GET", "/api/v1/assistance-authorizations/4d124e1d-652f-4b45-9187-f0dd7cb1ea49")]
  [InlineData("DELETE", "/api/v1/assistance-authorizations/4d124e1d-652f-4b45-9187-f0dd7cb1ea49")]
  public void IsAllowed_StableStationAdapterRoutes_ReturnsTrue(string method, string path)
  {
    Assert.True(StableStationServiceAccountHttpGuardMiddleware.IsAllowed(method, path));
  }
}
