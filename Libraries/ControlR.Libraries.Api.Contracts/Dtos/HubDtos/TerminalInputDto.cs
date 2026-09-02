namespace ControlR.Libraries.Api.Contracts.Dtos.HubDtos;
[MessagePackObject(keyAsPropertyName: true)]
public record TerminalInputDto(
    Guid TerminalId,
    string Input)
{
  public Guid? AssistanceAuthorizationId { get; init; }
  public Guid? AssistanceConnectorInstanceId { get; init; }
  public long? AssistanceEnableGeneration { get; init; }
  public string? ViewerConnectionId { get; set; }
}
