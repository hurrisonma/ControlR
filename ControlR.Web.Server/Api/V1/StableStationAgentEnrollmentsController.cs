using System.Text.RegularExpressions;
using Asp.Versioning;
using ControlR.Libraries.Api.Contracts.Constants;
using ControlR.Web.Server.Options;
using ControlR.Web.Server.Services.AgentInstaller;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ControlR.Web.Server.Api.V1;

[Route(HttpConstants.V1.StableStationAgentEnrollmentsEndpoint)]
[ApiController]
[Authorize(Policy = RequireServerServiceAccountPolicy.PolicyName)]
[ApiVersion(ApiVersions.V1)]
public partial class StableStationAgentEnrollmentsController(
  TimeProvider timeProvider,
  AppDb appDb,
  IAgentInstallerKeyManager installerKeyManager,
  IOptions<BootstrapOptions> bootstrapOptions) : ControllerBase
{
  private readonly AppDb _appDb = appDb;
  private readonly BootstrapOptions _bootstrapOptions = bootstrapOptions.Value;
  private readonly IAgentInstallerKeyManager _installerKeyManager = installerKeyManager;
  private readonly TimeProvider _timeProvider = timeProvider;

  [HttpPost]
  public async Task<ActionResult<V1Dtos.CreateStableStationAgentEnrollmentResponseDto>> Create(
    [FromBody] V1Dtos.CreateStableStationAgentEnrollmentRequestDto request)
  {
    var endpointId = request.EndpointId.Trim();
    if (!EndpointIdRegex().IsMatch(endpointId))
    {
      return BadRequest(new { error = "endpoint_id_invalid" });
    }

    var serviceAccountId = _bootstrapOptions.ServerServiceAccountId;
    var adminEmail = _bootstrapOptions.AdminEmail?.Trim();
    if (!serviceAccountId.HasValue ||
        serviceAccountId.Value == Guid.Empty ||
        string.IsNullOrWhiteSpace(adminEmail))
    {
      return StatusCode(
        StatusCodes.Status503ServiceUnavailable,
        new { error = "stablestation_enrollment_not_configured" });
    }

    var normalizedEmail = adminEmail.ToUpperInvariant();
    var tenantId = await _appDb.Users
      .Where(x => x.NormalizedEmail == normalizedEmail)
      .Select(x => x.TenantId)
      .SingleOrDefaultAsync();
    if (tenantId == Guid.Empty)
    {
      return StatusCode(
        StatusCodes.Status503ServiceUnavailable,
        new { error = "stablestation_enrollment_tenant_unavailable" });
    }

    var expiresAt = _timeProvider.GetUtcNow().AddMinutes(10);
    var key = await _installerKeyManager.CreateKey(
      tenantId,
      serviceAccountId.Value,
      CreatorKind.ServiceAccount,
      InstallerKeyType.UsageBased,
      1,
      expiresAt,
      $"StableStation endpoint {endpointId}");

    return Ok(new V1Dtos.CreateStableStationAgentEnrollmentResponseDto(
      tenantId,
      key.Id,
      key.KeySecret,
      key.Expiration ?? expiresAt));
  }

  [GeneratedRegex(@"^[A-Za-z0-9._:@-]{1,160}$")]
  private static partial Regex EndpointIdRegex();
}
