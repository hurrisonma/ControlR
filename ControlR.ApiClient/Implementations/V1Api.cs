using ControlR.ApiClient.Interfaces.V1;

namespace ControlR.ApiClient;

internal partial class V1Api(ControlrApi client) :
  IControlrV1Api,
  IAssistanceAuthorizationsApi,
  IDevicesApi,
  IInstallerKeysApi,
  ILogonTokensApi,
  IServiceAccountsApi,
  IStableStationAgentEnrollmentsApi,
  ITenantsApi
{
  private readonly ControlrApi _client = client;

  public IAssistanceAuthorizationsApi AssistanceAuthorizations => this;
  public IDevicesApi Devices => this;
  public IInstallerKeysApi InstallerKeys => this;
  public ILogonTokensApi LogonTokens => this;
  public IServiceAccountsApi ServiceAccounts => this;
  public IStableStationAgentEnrollmentsApi StableStationAgentEnrollments => this;
  public ITenantsApi Tenants => this;
}
