using ControlR.Libraries.Api.Contracts.Constants;
using ControlR.Libraries.Api.Contracts.Dtos;
using AssistanceDtos = ControlR.Libraries.Api.Contracts.Dtos.ServerApi.V1.AssistanceAuthorizations;

namespace ControlR.ApiClient.Interfaces.V1;

public interface IAssistanceAuthorizationsApi
{
  [ApiRoute($"{HttpConstants.V1.AssistanceAuthorizationsEndpoint}", "POST")]
  Task<ApiResult<AssistanceDtos.CreateAssistanceAuthorizationResponseDto>> CreateAssistanceAuthorization(AssistanceDtos.CreateAssistanceAuthorizationRequestDto request, CancellationToken cancellationToken = default);
  [ApiRoute($"{HttpConstants.V1.AssistanceAuthorizationsEndpoint}/{{authorizationId}}", "GET")]
  Task<ApiResult<AssistanceDtos.AssistanceAuthorizationDto>> GetAssistanceAuthorization(Guid authorizationId, CancellationToken cancellationToken = default);
  [ApiRoute($"{HttpConstants.V1.AssistanceAuthorizationsEndpoint}/{{authorizationId}}", "DELETE")]
  Task<ApiResult> RevokeAssistanceAuthorization(Guid authorizationId, CancellationToken cancellationToken = default);
}
