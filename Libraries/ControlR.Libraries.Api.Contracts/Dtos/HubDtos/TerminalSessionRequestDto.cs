namespace ControlR.Libraries.Api.Contracts.Dtos.HubDtos;

[MessagePackObject(keyAsPropertyName: true)]
public record TerminalSessionRequestDto(
  Guid TerminalSessionId,
  string ViewerConnectionId)
{
  public Guid? AssistanceAuthorizationId { get; init; }
  public Guid? AssistanceConnectorInstanceId { get; init; }
  public long? AssistanceEnableGeneration { get; init; }
}
