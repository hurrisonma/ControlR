namespace ControlR.Agent.Common.Configuration;

public class StableStationAssistanceGateOptions
{
  public const string SectionKey = "StableStationAssistanceGate";

  public bool Enabled { get; set; }
  public string EndpointId { get; set; } = string.Empty;
  public int Port { get; set; }
  public string SharedSecretFile { get; set; } = string.Empty;
}
