using System.Net.Http.Json;
using ControlR.ApiClient.Interfaces.V1;
using ControlR.Libraries.Api.Contracts.Constants;
using ControlR.Libraries.Api.Contracts.Dtos;
using AssistanceDtos = ControlR.Libraries.Api.Contracts.Dtos.ServerApi.V1.AssistanceAuthorizations;

namespace ControlR.ApiClient;

internal partial class V1Api
{
  async Task<ApiResult<AssistanceDtos.CreateAssistanceAuthorizationResponseDto>> IAssistanceAuthorizationsApi.CreateAssistanceAuthorization(AssistanceDtos.CreateAssistanceAuthorizationRequestDto request, CancellationToken cancellationToken)
  {
    return await _client.ExecuteApiCall(async () =>
    {
      using var response = await _client.HttpClient.PostAsJsonAsync(HttpConstants.V1.AssistanceAuthorizationsEndpoint, request, cancellationToken);
      await response.EnsureSuccessStatusCodeWithDetails();
      return await response.Content.ReadFromJsonAsync<AssistanceDtos.CreateAssistanceAuthorizationResponseDto>(cancellationToken);
    });
  }

  async Task<ApiResult<AssistanceDtos.AssistanceAuthorizationDto>> IAssistanceAuthorizationsApi.GetAssistanceAuthorization(Guid authorizationId, CancellationToken cancellationToken)
  {
    return await _client.ExecuteApiCall(async () =>
    {
      using var response = await _client.HttpClient.GetAsync($"{HttpConstants.V1.AssistanceAuthorizationsEndpoint}/{authorizationId}", cancellationToken);
      await response.EnsureSuccessStatusCodeWithDetails();
      return await response.Content.ReadFromJsonAsync<AssistanceDtos.AssistanceAuthorizationDto>(cancellationToken);
    });
  }

  async Task<ApiResult> IAssistanceAuthorizationsApi.RevokeAssistanceAuthorization(Guid authorizationId, CancellationToken cancellationToken)
  {
    return await _client.ExecuteApiCall(async () =>
    {
      using var response = await _client.HttpClient.DeleteAsync($"{HttpConstants.V1.AssistanceAuthorizationsEndpoint}/{authorizationId}", cancellationToken);
      await response.EnsureSuccessStatusCodeWithDetails();
    });
  }
}
