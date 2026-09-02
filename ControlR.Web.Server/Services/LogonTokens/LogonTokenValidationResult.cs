using System.Diagnostics.CodeAnalysis;

namespace ControlR.Web.Server.Services.LogonTokens;

public class LogonTokenValidationResult
{
  public Guid? AssistanceAuthorizationId { get; set; }
  public Guid? AssistanceConnectorInstanceId { get; set; }
  public long? AssistanceEnableGeneration { get; set; }
  public LogonTokenCapability? Capability { get; set; }
  public string? ErrorMessage { get; set; }
  [MemberNotNullWhen(true, nameof(UserId), nameof(TenantId))]
  public bool IsValid { get; set; }
  public string? SessionCorrelationId { get; set; }
  public Guid? TenantId { get; set; }

  public Guid? UserId { get; set; }

  public static LogonTokenValidationResult Failure(string errorMessage)
  {
    return new LogonTokenValidationResult
    {
      IsValid = false,
      ErrorMessage = errorMessage
    };
  }

  public static LogonTokenValidationResult Success(
    Guid userId,
    Guid tenantId,
    string? sessionCorrelationId = null,
    LogonTokenCapability? capability = null,
    Guid? assistanceAuthorizationId = null,
    Guid? assistanceConnectorInstanceId = null,
    long? assistanceEnableGeneration = null)
  {
    return new LogonTokenValidationResult
    {
      AssistanceAuthorizationId = assistanceAuthorizationId,
      AssistanceConnectorInstanceId = assistanceConnectorInstanceId,
      AssistanceEnableGeneration = assistanceEnableGeneration,
      Capability = capability,
      IsValid = true,
      UserId = userId,
      TenantId = tenantId,
      SessionCorrelationId = sessionCorrelationId
    };
  }
}
