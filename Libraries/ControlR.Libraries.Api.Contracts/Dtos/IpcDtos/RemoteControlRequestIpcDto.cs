namespace ControlR.Libraries.Api.Contracts.Dtos.IpcDtos;

[MessagePackObject(keyAsPropertyName: true)]
public record RemoteControlRequestIpcDto(
  Guid SessionId,
  Uri WebsocketUri,
  int TargetSystemSession,
  int TargetProcessId,
  Guid DeviceId,
  bool NotifyUserOnSessionStart,
  bool RequireConsent,
  string? ViewerConnectionId,
  string? ViewerName,
  Guid? AssistanceAuthorizationId,
  Guid? AssistanceConnectorInstanceId,
  long? AssistanceEnableGeneration);
