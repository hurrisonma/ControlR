using System.ComponentModel.DataAnnotations;

namespace ControlR.Libraries.Api.Contracts.Dtos.ServerApi.V1;

public record CreateStableStationAgentEnrollmentRequestDto(
  [property: Required]
  string EndpointId);
