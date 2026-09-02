using ControlR.Libraries.Api.Contracts.Enums;

namespace ControlR.Libraries.Api.Contracts.Dtos.ServerApi.V1.AssistanceAuthorizations;

public record AssistanceAuthorizationDto(
  Guid AuthorizationId,
  Guid DeviceId,
  Guid TenantId,
  string EndpointId,
  string ConnectorSessionId,
  Guid ConnectorInstanceId,
  long EnableGeneration,
  string UserCorrelationId,
  string UserDisplayName,
  string SessionCorrelationId,
  LogonTokenCapability Capability,
  AssistanceAuthorizationStatus Status,
  DateTimeOffset CreatedAt,
  DateTimeOffset ExpiresAt,
  DateTimeOffset? ConnectedAt,
  DateTimeOffset? ClosedAt,
  string? CloseReason);
