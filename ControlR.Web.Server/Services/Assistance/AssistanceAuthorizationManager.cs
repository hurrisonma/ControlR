using ControlR.Libraries.Api.Contracts.Dtos.ServerApi.V1.AssistanceAuthorizations;
using ControlR.Libraries.Api.Contracts.Enums;
using ControlR.Web.Server.Primitives;
using ControlR.Web.Server.Services.LogonTokens;
using Npgsql;
using System.Security.Claims;

namespace ControlR.Web.Server.Services.Assistance;

public class AssistanceAuthorizationManager(
  TimeProvider timeProvider,
  IDbContextFactory<AppDb> dbContextFactory,
  ILogonTokenProvider logonTokenProvider,
  AssistanceViewerConnectionRegistry connectionRegistry,
  ILogger<AssistanceAuthorizationManager> logger) : IAssistanceAuthorizationManager
{
  private static readonly AssistanceAuthorizationStatus[] _activeStatuses =
  [
    AssistanceAuthorizationStatus.Pending,
    AssistanceAuthorizationStatus.Connected
  ];

  private readonly AssistanceViewerConnectionRegistry _connectionRegistry = connectionRegistry;
  private readonly IDbContextFactory<AppDb> _dbContextFactory = dbContextFactory;
  private readonly ILogger<AssistanceAuthorizationManager> _logger = logger;
  private readonly ILogonTokenProvider _logonTokenProvider = logonTokenProvider;
  private readonly TimeProvider _timeProvider = timeProvider;

  public async Task<HttpResult<AssistanceAuthorizationCreation>> Create(
    CreateAssistanceAuthorizationRequestDto request,
    CancellationToken cancellationToken = default)
  {
    var endpointId = request.EndpointId.Trim();
    var connectorSessionId = request.ConnectorSessionId.Trim();
    var userCorrelationId = request.UserCorrelationId.Trim();
    var userDisplayName = request.UserDisplayName.Trim();
    var sessionCorrelationId = request.SessionCorrelationId.Trim();

    if (endpointId.Length is 0 or > 200 ||
        connectorSessionId.Length is 0 or > 200 ||
        userCorrelationId.Length is 0 or > 200 ||
        userDisplayName.Length is 0 or > 200 ||
        sessionCorrelationId.Length is 0 or > 200 ||
        request.ConnectorInstanceId == Guid.Empty ||
        request.EnableGeneration <= 0 ||
        request.ExpirationMinutes is < 1 or > 15)
    {
      return HttpResult.Fail<AssistanceAuthorizationCreation>(
        HttpResultErrorCode.BadRequest,
        "Invalid remote assistance authorization request.");
    }

    await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
    var device = await dbContext.Devices.FirstOrDefaultAsync(
      x => x.Id == request.DeviceId && x.TenantId == request.TenantId,
      cancellationToken);
    if (device is null)
    {
      return HttpResult.Fail<AssistanceAuthorizationCreation>(
        HttpResultErrorCode.NotFound,
        "Device was not found in the requested tenant.");
    }
    if (!device.IsOnline)
    {
      return HttpResult.Fail<AssistanceAuthorizationCreation>(
        HttpResultErrorCode.Conflict,
        "Device agent is offline.");
    }

    var now = _timeProvider.GetUtcNow();
    var existing = await dbContext.AssistanceAuthorizations
      .Where(x => x.EndpointId == endpointId && _activeStatuses.Contains(x.Status))
      .ToListAsync(cancellationToken);
    foreach (var current in existing)
    {
      current.Status = AssistanceAuthorizationStatus.Revoked;
      current.ClosedAt = now;
      current.CloseReason = "superseded";
    }

    var authorization = new AssistanceAuthorization
    {
      Capability = LogonTokenCapability.RemoteSupport,
      ConnectorInstanceId = request.ConnectorInstanceId,
      ConnectorSessionId = connectorSessionId,
      DeviceId = request.DeviceId,
      EnableGeneration = request.EnableGeneration,
      EndpointId = endpointId,
      ExpiresAt = now.AddMinutes(request.ExpirationMinutes),
      SessionCorrelationId = sessionCorrelationId,
      Status = AssistanceAuthorizationStatus.Pending,
      TenantId = request.TenantId,
      UserCorrelationId = userCorrelationId,
      UserDisplayName = userDisplayName
    };
    if (existing.Count > 0)
    {
      await dbContext.SaveChangesAsync(cancellationToken);
    }
    dbContext.AssistanceAuthorizations.Add(authorization);
    try
    {
      await dbContext.SaveChangesAsync(cancellationToken);
    }
    catch (DbUpdateException ex) when (
      ex.InnerException is PostgresException
      {
        SqlState: PostgresErrorCodes.UniqueViolation
      })
    {
      return HttpResult.Fail<AssistanceAuthorizationCreation>(
        HttpResultErrorCode.Conflict,
        "Another active authorization already exists for the endpoint.");
    }

    foreach (var current in existing)
    {
      _connectionRegistry.Abort(current.Id);
    }

    var tokenResult = await _logonTokenProvider.CreateTokenForExternal(
      authorization.DeviceId,
      authorization.TenantId,
      authorization.UserCorrelationId,
      authorization.Capability,
      request.ExpirationMinutes,
      authorization.UserDisplayName,
      authorization.SessionCorrelationId,
      authorization.Id,
      authorization.ConnectorInstanceId,
      authorization.EnableGeneration,
      cancellationToken);

    if (!tokenResult.IsSuccess)
    {
      authorization.Status = AssistanceAuthorizationStatus.Revoked;
      authorization.ClosedAt = _timeProvider.GetUtcNow();
      authorization.CloseReason = "token_issuance_failed";
      await dbContext.SaveChangesAsync(cancellationToken);
      return HttpResult.Fail<AssistanceAuthorizationCreation>(tokenResult.ErrorCode, tokenResult.Reason);
    }

    _logger.LogInformation(
      "Created assistance authorization {AuthorizationId} for device {DeviceId}, endpoint {EndpointId}, generation {Generation}.",
      authorization.Id,
      authorization.DeviceId,
      authorization.EndpointId,
      authorization.EnableGeneration);

    return HttpResult.Ok(new AssistanceAuthorizationCreation(ToDto(authorization), tokenResult.Value));
  }

  public async Task<int> ExpireStale(CancellationToken cancellationToken = default)
  {
    var now = _timeProvider.GetUtcNow();
    await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
    var expired = await dbContext.AssistanceAuthorizations
      .Where(x => _activeStatuses.Contains(x.Status) && x.ExpiresAt <= now)
      .ToListAsync(cancellationToken);
    foreach (var authorization in expired)
    {
      authorization.Status = AssistanceAuthorizationStatus.Expired;
      authorization.ClosedAt = now;
      authorization.CloseReason = "expired";
    }
    if (expired.Count == 0)
    {
      return 0;
    }

    await dbContext.SaveChangesAsync(cancellationToken);
    foreach (var authorization in expired)
    {
      _connectionRegistry.Abort(authorization.Id);
    }
    _logger.LogInformation(
      "Expired and disconnected {AuthorizationCount} remote assistance authorizations.",
      expired.Count);
    return expired.Count;
  }

  public async Task<AssistanceAuthorizationDto?> Get(
    Guid authorizationId,
    CancellationToken cancellationToken = default)
  {
    await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
    var authorization = await dbContext.AssistanceAuthorizations
      .FirstOrDefaultAsync(x => x.Id == authorizationId, cancellationToken);
    if (authorization is null)
    {
      return null;
    }
    if (_activeStatuses.Contains(authorization.Status) &&
        authorization.ExpiresAt <= _timeProvider.GetUtcNow())
    {
      authorization.Status = AssistanceAuthorizationStatus.Expired;
      authorization.ClosedAt = _timeProvider.GetUtcNow();
      authorization.CloseReason = "expired";
      await dbContext.SaveChangesAsync(cancellationToken);
      _connectionRegistry.Abort(authorization.Id);
    }
    return ToDto(authorization);
  }

  public async Task<bool> IsActive(
    ClaimsPrincipal? principal,
    CancellationToken cancellationToken = default)
  {
    if (principal is null ||
        !Guid.TryParse(principal.FindFirstValue(UserClaimTypes.AssistanceAuthorizationId), out var authorizationId) ||
        !Guid.TryParse(principal.FindFirstValue(UserClaimTypes.AssistanceConnectorInstanceId), out var connectorInstanceId) ||
        !long.TryParse(principal.FindFirstValue(UserClaimTypes.AssistanceEnableGeneration), out var enableGeneration) ||
        !Guid.TryParse(principal.FindFirstValue(UserClaimTypes.DeviceSessionScope), out var deviceId))
    {
      return false;
    }

    await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
    var authorization = await dbContext.AssistanceAuthorizations
      .FirstOrDefaultAsync(x => x.Id == authorizationId, cancellationToken);
    if (authorization is null ||
        authorization.DeviceId != deviceId ||
        authorization.ConnectorInstanceId != connectorInstanceId ||
        authorization.EnableGeneration != enableGeneration ||
        !_activeStatuses.Contains(authorization.Status))
    {
      return false;
    }

    if (authorization.ExpiresAt > _timeProvider.GetUtcNow())
    {
      return true;
    }

    authorization.Status = AssistanceAuthorizationStatus.Expired;
    authorization.ClosedAt = _timeProvider.GetUtcNow();
    authorization.CloseReason = "expired";
    await dbContext.SaveChangesAsync(cancellationToken);
    _connectionRegistry.Abort(authorization.Id);
    return false;
  }

  public async Task MarkConnected(
    Guid authorizationId,
    string connectionId,
    CancellationToken cancellationToken = default)
  {
    await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
    var authorization = await dbContext.AssistanceAuthorizations
      .FirstOrDefaultAsync(x => x.Id == authorizationId, cancellationToken);
    if (authorization is null || !_activeStatuses.Contains(authorization.Status))
    {
      return;
    }

    authorization.Status = AssistanceAuthorizationStatus.Connected;
    authorization.ConnectedAt ??= _timeProvider.GetUtcNow();
    authorization.ViewerConnectionId = connectionId;
    await dbContext.SaveChangesAsync(cancellationToken);
  }

  public async Task MarkEnded(
    Guid authorizationId,
    string reason,
    CancellationToken cancellationToken = default)
  {
    await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
    var authorization = await dbContext.AssistanceAuthorizations
      .FirstOrDefaultAsync(x => x.Id == authorizationId, cancellationToken);
    if (authorization is null || !_activeStatuses.Contains(authorization.Status))
    {
      return;
    }

    var cleanReason = string.IsNullOrWhiteSpace(reason) ? "ended" : reason.Trim();
    authorization.Status = AssistanceAuthorizationStatus.Ended;
    authorization.ClosedAt = _timeProvider.GetUtcNow();
    authorization.CloseReason = cleanReason[..Math.Min(cleanReason.Length, 200)];
    authorization.ViewerConnectionId = null;
    await dbContext.SaveChangesAsync(cancellationToken);
  }

  public async Task<bool> Revoke(
    Guid authorizationId,
    string reason,
    CancellationToken cancellationToken = default)
  {
    await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
    var authorization = await dbContext.AssistanceAuthorizations
      .FirstOrDefaultAsync(x => x.Id == authorizationId, cancellationToken);
    if (authorization is null)
    {
      return false;
    }

    if (_activeStatuses.Contains(authorization.Status))
    {
      var cleanReason = string.IsNullOrWhiteSpace(reason) ? "revoked" : reason.Trim();
      authorization.Status = AssistanceAuthorizationStatus.Revoked;
      authorization.ClosedAt = _timeProvider.GetUtcNow();
      authorization.CloseReason = cleanReason[..Math.Min(cleanReason.Length, 200)];
      await dbContext.SaveChangesAsync(cancellationToken);
    }

    _connectionRegistry.Abort(authorization.Id);
    return true;
  }

  private static AssistanceAuthorizationDto ToDto(AssistanceAuthorization authorization)
  {
    return new AssistanceAuthorizationDto(
      authorization.Id,
      authorization.DeviceId,
      authorization.TenantId,
      authorization.EndpointId,
      authorization.ConnectorSessionId,
      authorization.ConnectorInstanceId,
      authorization.EnableGeneration,
      authorization.UserCorrelationId,
      authorization.UserDisplayName,
      authorization.SessionCorrelationId,
      authorization.Capability,
      authorization.Status,
      authorization.CreatedAt,
      authorization.ExpiresAt,
      authorization.ConnectedAt,
      authorization.ClosedAt,
      authorization.CloseReason);
  }
}
