using System.Collections.Concurrent;

namespace ControlR.Web.Server.Services.Assistance;

public class AssistanceViewerConnectionRegistry
{
  private readonly ConcurrentDictionary<Guid, ViewerConnection> _connections = [];

  public void Abort(Guid authorizationId)
  {
    if (!_connections.TryRemove(authorizationId, out var connections))
    {
      return;
    }

    connections.Abort();
  }

  public bool TryRegister(Guid authorizationId, string connectionId, Action abort)
  {
    return _connections.TryAdd(
      authorizationId,
      new ViewerConnection(connectionId, abort));
  }

  public void Unregister(Guid authorizationId, string connectionId)
  {
    if (!_connections.TryGetValue(authorizationId, out var connection) ||
        connection.ConnectionId != connectionId)
    {
      return;
    }
    _connections.TryRemove(authorizationId, out _);
  }

  private sealed record ViewerConnection(string ConnectionId, Action Abort);
}
