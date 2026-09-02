namespace ControlR.Libraries.Api.Contracts.Dtos.HubDtos.PwshCommandCompletions;

public record PwshCompletionsRequestDto(
  Guid DeviceId,
  Guid TerminalId,
  string LastCompletionInput,
  int LastCursorIndex,
  string ViewerConnectionId,
  bool? Forward,
  int Page = 0,
  int PageSize = PwshCompletionsRequestDto.DefaultPageSize)
{
  public const int DefaultPageSize = 50;
  public Guid? AssistanceAuthorizationId { get; init; }
  public Guid? AssistanceConnectorInstanceId { get; init; }
  public long? AssistanceEnableGeneration { get; init; }
}
