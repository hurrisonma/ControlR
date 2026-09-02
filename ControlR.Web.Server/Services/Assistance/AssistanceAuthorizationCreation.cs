using ControlR.Libraries.Api.Contracts.Dtos.ServerApi.V1.AssistanceAuthorizations;
using ControlR.Web.Server.Services.LogonTokens;

namespace ControlR.Web.Server.Services.Assistance;

public record AssistanceAuthorizationCreation(
  AssistanceAuthorizationDto Authorization,
  LogonTokenModel LogonToken);
