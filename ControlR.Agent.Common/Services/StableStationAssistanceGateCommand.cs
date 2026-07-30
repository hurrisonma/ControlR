namespace ControlR.Agent.Common.Services;

public record StableStationAssistanceGateCommand(
  string Action,
  string EndpointId,
  Guid ConnectorInstanceId,
  long EnableGeneration,
  long LeaseExpiresAtUnixSeconds,
  string Nonce,
  string Signature);
