namespace ControlR.Libraries.Api.Contracts.Dtos.ServerApi.V1.AssistanceAuthorizations;

public record CreateAssistanceAuthorizationRequestDto(
  Guid DeviceId,
  Guid TenantId,
  string EndpointId,
  string ConnectorSessionId,
  Guid ConnectorInstanceId,
  long EnableGeneration,
  string UserCorrelationId,
  string UserDisplayName,
  string SessionCorrelationId,
  int ExpirationMinutes);
