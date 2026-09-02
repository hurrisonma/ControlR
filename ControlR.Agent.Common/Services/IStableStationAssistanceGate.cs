using System.Threading.Channels;
using ControlR.Libraries.Api.Contracts.Dtos.RemoteControlDtos;

namespace ControlR.Agent.Common.Services;

public interface IStableStationAssistanceGate
{
  ChannelReader<string> Closures { get; }
  bool Apply(StableStationAssistanceGateCommand command, ReadOnlySpan<byte> sharedSecret, out string reason);
  void ExpireIfRequired();
  bool IsAllowed(
    Guid? assistanceAuthorizationId,
    Guid? assistanceConnectorInstanceId,
    long? assistanceEnableGeneration,
    out string reason);
  bool IsAllowed(RemoteControlSessionRequestDto request, out string reason);
}
