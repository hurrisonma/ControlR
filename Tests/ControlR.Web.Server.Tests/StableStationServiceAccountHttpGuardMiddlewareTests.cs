using ControlR.Web.Server.Middleware;

namespace ControlR.Web.Server.Tests;

public class StableStationServiceAccountHttpGuardMiddlewareTests
{
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
  [InlineData("GET", "/api/v1/assistance-authorizations/4d124e1d-652f-4b45-9187-f0dd7cb1ea49")]
  [InlineData("DELETE", "/api/v1/assistance-authorizations/4d124e1d-652f-4b45-9187-f0dd7cb1ea49")]
  public void IsAllowed_StableStationAdapterRoutes_ReturnsTrue(string method, string path)
  {
    Assert.True(StableStationServiceAccountHttpGuardMiddleware.IsAllowed(method, path));
  }
}
