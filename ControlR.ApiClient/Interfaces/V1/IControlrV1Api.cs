namespace ControlR.ApiClient.Interfaces.V1;

public interface IControlrV1Api
{
  IAssistanceAuthorizationsApi AssistanceAuthorizations { get; }
  IDevicesApi Devices { get; }
  IInstallerKeysApi InstallerKeys { get; }
  ILogonTokensApi LogonTokens { get; }
  IServiceAccountsApi ServiceAccounts { get; }
  IStableStationAgentEnrollmentsApi StableStationAgentEnrollments { get; }
  ITenantsApi Tenants { get; }
}
