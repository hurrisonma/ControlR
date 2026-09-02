using ControlR.Libraries.Api.Contracts.Dtos.ServerApi.V1.AssistanceAuthorizations;
using ControlR.Web.Server.Primitives;
using System.Security.Claims;

namespace ControlR.Web.Server.Services.Assistance;

public interface IAssistanceAuthorizationManager
{
  Task<HttpResult<AssistanceAuthorizationCreation>> Create(
    CreateAssistanceAuthorizationRequestDto request,
    CancellationToken cancellationToken = default);
  Task<int> ExpireStale(CancellationToken cancellationToken = default);
  Task<AssistanceAuthorizationDto?> Get(Guid authorizationId, CancellationToken cancellationToken = default);
  Task<bool> IsActive(ClaimsPrincipal? principal, CancellationToken cancellationToken = default);
  Task MarkConnected(Guid authorizationId, string connectionId, CancellationToken cancellationToken = default);
  Task MarkEnded(Guid authorizationId, string reason, CancellationToken cancellationToken = default);
  Task<bool> Revoke(Guid authorizationId, string reason, CancellationToken cancellationToken = default);
}
