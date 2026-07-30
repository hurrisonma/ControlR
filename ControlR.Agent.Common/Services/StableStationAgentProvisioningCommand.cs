namespace ControlR.Agent.Common.Services;

public record StableStationAgentProvisioningCommand(
  string EndpointId,
  Guid TenantId,
  Guid InstallerKeyId,
  string InstallerKeySecret,
  long IssuedAtUnixSeconds,
  string Nonce,
  string Signature);
