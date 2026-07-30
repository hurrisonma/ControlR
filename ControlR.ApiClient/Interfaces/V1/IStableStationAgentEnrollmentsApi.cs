using ControlR.Libraries.Api.Contracts.Constants;
using ControlR.Libraries.Api.Contracts.Dtos;
using V1Dtos = ControlR.Libraries.Api.Contracts.Dtos.ServerApi.V1;

namespace ControlR.ApiClient.Interfaces.V1;

public interface IStableStationAgentEnrollmentsApi
{
  [ApiRoute($"{HttpConstants.V1.StableStationAgentEnrollmentsEndpoint}", "POST")]
  Task<ApiResult<V1Dtos.CreateStableStationAgentEnrollmentResponseDto>> CreateStableStationAgentEnrollment(
    V1Dtos.CreateStableStationAgentEnrollmentRequestDto request,
    CancellationToken cancellationToken = default);
}
