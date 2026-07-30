# StableStation Remote Assistance fork

This branch turns ControlR into the isolated desktop transport for StableStation Remote Assistance.
It intentionally does not expose ControlR's general RMM capabilities through StableStation sessions.

## Server authorization

The versioned V1 endpoints are:

- `POST /api/v1/stablestation/agent-enrollments`
- `POST /api/v1/assistance-authorizations`
- `GET /api/v1/assistance-authorizations/{authorizationId}`
- `DELETE /api/v1/assistance-authorizations/{authorizationId}`

They require the server-scoped service-account policy. The enrollment endpoint derives the tenant
from the bootstrap service account and creates an expiring, one-use Agent installer credential;
StableStation cannot use that account to call the general installer-key API. Authorization creation
verifies that the requested device belongs to the requested tenant and is online. The server fixes
the capability to `RemoteDesktop`, limits expiration to 15 minutes, stores the Connector process
instance and enable generation, and enforces at most one active authorization per StableStation
endpoint with a PostgreSQL partial unique index.

The configured bootstrap Server Service Account is additionally constrained by
`StableStationServiceAccountHttpGuardMiddleware`. It can only create a dedicated one-use Agent
enrollment, read one device by ID, and create, read, or revoke assistance authorizations. Tenant,
device deletion/list, general installer-key, general logon-token, and service-account management
endpoints are denied even if this key is used outside the StableStation adapter.

An expiration background service sweeps every five seconds. Expiration, revocation, replacement,
or Viewer disconnect updates the durable authorization status and aborts the active ViewerHub
connection. A single authorization permits only one ViewerHub connection.

## Browser capability boundary

The one-time external logon token carries the authorization ID, device scope, Connector instance,
generation, user/session correlation, and the single `RemoteDesktop` capability.

`RemoteDesktopCapabilityHttpGuardMiddleware` rejects non-allowlisted API access for that session.
`ViewerHubCapabilityFilter` admits only the desktop methods required by the browser Viewer and
re-checks the durable authorization on every invocation. Terminal, file, log, script, VNC, device,
tenant, and service-account operations are outside the allowlist.

The ViewerHub overwrites assistance fields supplied by the browser with authenticated claims before
it forwards `RemoteControlSessionRequestDto` to the Agent.

## Mandatory Agent gate

The Agent binds an HTTP listener only to loopback. It accepts `POST /v1/gate` commands signed with
HMAC-SHA256 over the action, endpoint, Connector instance, generation, lease expiry, and nonce. It
also accepts signed `POST /v1/provision` requests carrying the one-use enrollment only while no
Agent identity exists; repeating the same already-provisioned identity is idempotent.

- Shared secrets must contain at least 32 bytes.
- Enable and heartbeat leases must expire within 45 seconds.
- A nonce cannot be replayed for two minutes.
- Heartbeat and disable must match the exact active instance and generation.
- Requests are limited to 8 KiB, a three-second deadline, and eight concurrent local clients.
- The in-memory gate is closed on every Agent start.
- A disabled or unconfigured gate rejects remote control; this fork does not fall back to upstream
  unattended desktop behavior.
- Disable, generation replacement, or lease expiration requests shutdown of every DesktopClient.

The StableStation Trading Connector installer is the only production installer. It deploys the
private Agent/DesktopClient runtime, preserves Agent identity in the normal ControlR appsettings,
rewrites the later-loaded StableStation managed config and Gate options on every install, creates the
delayed-auto `StableStation.TradingConnector.RemoteAssistance` LocalSystem service, and owns the ACL contract.
The Agent service stays running, but every Agent start closes the in-memory Gate. Connector update
first disables the current Gate/Viewer, then the unified installer stops the service before replacing
the runtime. Uninstall removes the service, identity, and Gate configuration.

## Publishing

`.github/workflows/publish-stablestation-ghcr.yml` validates the StableStation-specific Server and
Agent tests, then builds the Server and matching Windows x64 Agent/DesktopClient bundle from one
exact revision. It publishes the Server as an immutable
`ghcr.io/hurrisonma/controlr-server:sha-<full-commit>` Linux AMD64 image with SBOM and provenance,
embeds the bundle in that image, and retains the same bundle as a workflow artifact. The bundle
contains only Agent, DesktopClient, a runtime manifest, and the MIT license; no standalone installer
is produced. StableStation records the resulting registry digest, exact source commit, bundle hash,
and runtime-manifest hash in `integrations/controlr/upstream.lock.json`.

The fork workflow's Windows binaries are unsigned internal-test artifacts. Production rollout
requires an approved StableStation code-signing identity. Do not deploy an upstream Agent with the
StableStation Server: it does not contain the mandatory local generation gate.
