using Asp.Versioning;
using ControlR.Libraries.Api.Contracts.Constants;
using ControlR.Libraries.Api.Contracts.Dtos.ServerApi.V1.AssistanceAuthorizations;
using ControlR.Web.Server.Services.Assistance;
using Microsoft.AspNetCore.Mvc;

namespace ControlR.Web.Server.Api.V1;

[Route(HttpConstants.V1.AssistanceAuthorizationsEndpoint)]
[ApiController]
[Authorize(Policy = RequireServerServiceAccountPolicy.PolicyName)]
[ApiVersion(ApiVersions.V1)]
public class AssistanceAuthorizationsController : ControllerBase
{
  [HttpPost]
  public async Task<ActionResult<CreateAssistanceAuthorizationResponseDto>> Create(
    [FromServices] IAssistanceAuthorizationManager manager,
    [FromBody] CreateAssistanceAuthorizationRequestDto request,
    CancellationToken cancellationToken)
  {
    var result = await manager.Create(request, cancellationToken);
    if (!result.IsSuccess)
    {
      return result.ToHttpResult().ToActionResult();
    }

    var token = result.Value.LogonToken;
    var deviceAccessUrl = new Uri(
      Request.ToOrigin(),
      $"/device-access?deviceId={token.DeviceId}&logonToken={token.Token}");
    return Ok(new CreateAssistanceAuthorizationResponseDto(
      result.Value.Authorization,
      deviceAccessUrl,
      token.ExpiresAt));
  }

  [HttpGet("{authorizationId:guid}")]
  public async Task<ActionResult<AssistanceAuthorizationDto>> Get(
    [FromServices] IAssistanceAuthorizationManager manager,
    Guid authorizationId,
    CancellationToken cancellationToken)
  {
    var authorization = await manager.Get(authorizationId, cancellationToken);
    return authorization is null ? NotFound() : Ok(authorization);
  }

  [HttpDelete("{authorizationId:guid}")]
  public async Task<IActionResult> Revoke(
    [FromServices] IAssistanceAuthorizationManager manager,
    Guid authorizationId,
    CancellationToken cancellationToken)
  {
    return await manager.Revoke(authorizationId, "service_account_revoked", cancellationToken)
      ? NoContent()
      : NotFound();
  }
}
