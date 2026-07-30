# StableStation Remote Assistance fork

This branch turns ControlR into the isolated desktop transport for StableStation Remote Assistance.
It intentionally does not expose ControlR's general RMM capabilities through StableStation sessions.

## Server authorization

The versioned V1 endpoints are:

- `POST /api/v1/assistance-authorizations`
- `GET /api/v1/assistance-authorizations/{authorizationId}`
- `DELETE /api/v1/assistance-authorizations/{authorizationId}`

They require the server-scoped service-account policy. Creation verifies that the requested device
belongs to the requested tenant and is online. The server fixes the capability to `RemoteDesktop`,
limits expiration to 15 minutes, stores the Connector process instance and enable generation, and
enforces at most one active authorization per StableStation endpoint with a PostgreSQL partial
unique index.

The configured bootstrap Server Service Account is additionally constrained by
`StableStationServiceAccountHttpGuardMiddleware`. It can only read one device by ID and create,
read, or revoke assistance authorizations. Tenant, device deletion/list, installer-key,
general logon-token, and service-account management endpoints are denied even if this key is used
outside the StableStation adapter.

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
HMAC-SHA256 over the action, endpoint, Connector instance, generation, lease expiry, and nonce.

- Shared secrets must contain at least 32 bytes.
- Enable and heartbeat leases must expire within 45 seconds.
- A nonce cannot be replayed for two minutes.
- Heartbeat and disable must match the exact active instance and generation.
- Requests are limited to 8 KiB, a three-second deadline, and eight concurrent local clients.
- The in-memory gate is closed on every Agent start.
- A disabled or unconfigured gate rejects remote control; this fork does not fall back to upstream
  unattended desktop behavior.
- Disable, generation replacement, or lease expiration requests shutdown of every DesktopClient.

Production installation writes the Agent options and the matching Connector configuration under
`%ProgramData%\\StableStation\\RemoteAssistance`. The StableStation repository owns that installer
script and ACL contract.

## Publishing

`.github/workflows/publish-stablestation-ghcr.yml` publishes the Server as an immutable
`ghcr.io/hurrisonma/controlr-server:sha-<full-commit>` Linux AMD64 image with SBOM and provenance.
StableStation records the resulting registry digest and exact source commit in
`integrations/controlr/upstream.lock.json`.

The Windows Agent and DesktopClient must be built and signed from the same reviewed fork revision.
Do not deploy an upstream Agent with the StableStation Server: it does not contain the mandatory
local generation gate.
