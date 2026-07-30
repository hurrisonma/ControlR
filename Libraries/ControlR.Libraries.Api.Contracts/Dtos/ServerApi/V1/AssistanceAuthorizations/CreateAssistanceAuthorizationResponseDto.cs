namespace ControlR.Libraries.Api.Contracts.Dtos.ServerApi.V1.AssistanceAuthorizations;

public record CreateAssistanceAuthorizationResponseDto(
  AssistanceAuthorizationDto Authorization,
  Uri DeviceAccessUrl,
  DateTimeOffset TokenExpiresAt);
