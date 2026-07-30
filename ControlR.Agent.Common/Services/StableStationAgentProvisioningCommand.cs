namespace ControlR.Agent.Common.Services;

public record StableStationAgentProvisioningCommand(
  string EndpointId,
  Guid TenantId,
  Guid InstallerKeyId,
  string InstallerKeySecret,
  Uri ServerUri,
  long IssuedAtUnixSeconds,
  string Nonce,
  string Signature);
