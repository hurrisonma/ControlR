using ControlR.Libraries.Api.Contracts.Enums;
using ControlR.Web.Server.Data.Entities.Bases;
using System.ComponentModel.DataAnnotations;

namespace ControlR.Web.Server.Data.Entities;

public class AssistanceAuthorization : TenantEntityBase
{
  public LogonTokenCapability Capability { get; set; }
  public DateTimeOffset? ClosedAt { get; set; }
  [MaxLength(200)]
  public string? CloseReason { get; set; }
  public DateTimeOffset? ConnectedAt { get; set; }
  public Guid ConnectorInstanceId { get; set; }
  [MaxLength(200)]
  public required string ConnectorSessionId { get; set; }
  public Guid DeviceId { get; set; }
  public long EnableGeneration { get; set; }
  [MaxLength(200)]
  public required string EndpointId { get; set; }
  public DateTimeOffset ExpiresAt { get; set; }
  [MaxLength(200)]
  public required string SessionCorrelationId { get; set; }
  public AssistanceAuthorizationStatus Status { get; set; }
  [MaxLength(200)]
  public required string UserCorrelationId { get; set; }
  [MaxLength(200)]
  public required string UserDisplayName { get; set; }
  [MaxLength(200)]
  public string? ViewerConnectionId { get; set; }
}
