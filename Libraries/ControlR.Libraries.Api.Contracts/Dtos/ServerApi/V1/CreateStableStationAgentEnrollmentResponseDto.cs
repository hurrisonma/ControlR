namespace ControlR.Libraries.Api.Contracts.Dtos.ServerApi.V1;

public record CreateStableStationAgentEnrollmentResponseDto(
  Guid TenantId,
  Guid InstallerKeyId,
  string InstallerKeySecret,
  DateTimeOffset ExpiresAt);
