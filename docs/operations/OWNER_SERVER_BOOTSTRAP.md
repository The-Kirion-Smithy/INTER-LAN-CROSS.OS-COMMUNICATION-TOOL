# INTER-LAN owner server: first-run and recovery

**Source baseline:** main after PRs #23, #24, #28, #29, #32, #33 (October 2026).
**Scope:** operator reference, not evidence of deployment acceptance.
**Source:** `src/InterLan.Desktop/OwnerPanel.cs`, `OwnerServerRuntime.cs`, `src/InterLan.Server/InterLanServerHost.cs`, `EnrollmentEndpointMappings.cs`, and `src/InterLan.Infrastructure/EnrollmentStore.cs`.

## What the owner host actually controls
The desktop owner mode hosts the canonical ASP.NET Core/Kestrel LAN server. Its SQLite database and TLS certificate are created/loaded under the configured data directory. The owner account is a persistent record in the server database, **not** whichever computer started the process. The configured bind address/port are obtained from persisted server settings and explicit runtime overrides; the local owner UI is a management surface, not a second authentication backend.

## First-run operator procedure
1. Start INTER-LAN Desktop and open **Owner server**. Select an appropriate durable data directory; do not use a temporary folder or a shared/public path. Provision appropriate OS filesystem protections and free disk space.
2. Start the owner host and wait for the real runtime status. A successful process start does **not** bootstrap the owner automatically.
3. Enter server display name, owner username/display name, and a strong password with confirmation in the first-run form. Submit **Create the owner account**. The backend accepts `POST /api/v1/bootstrap/server` only when the remote address is a loopback address. Do not expose this bootstrap operation through a reverse proxy on an untrusted network.
4. For an already configured server, **sign in as owner** with `POST /api/v1/auth/owner/login`; never reinitialize the database as a shortcut to recover credentials.
5. In authenticated owner controls, issue an invitation with `POST /api/v1/invites`, then inspect `GET /api/v1/join/pending`. An invite token is a short-lived, single-use secret.
6. Approve or reject individual join requests via `POST /api/v1/join/{requestId}/decision`. Review requested user/device information before deciding. The joining client then exchanges its approved request for a device-bound session; approval alone should not grant access.
7. In `GET /api/v1/devices`, inspect approved/revoked devices. Revoke suspicious devices via the owner-authorized device endpoint. Prefer revocation over deleting database rows.

## Restart, migration and data handling
- Stop the embedded owner server before moving a data directory or performing a filesystem copy. The SQLite file and TLS identity must be preserved together.
- Restart using the same durable directory to reuse the previously configured owner identity, device records and TLS key. A recreated certificate changes the fingerprint and causes correctly pinned native clients to reject the new identity.
- The server initializes the database through `SqliteDatabase.InitializeAsync`; migrations are source-managed. Never hand-edit schema indexes or silently replace a populated database during recovery.
- A port-in-use, invalid bind address, inaccessible path or certificate problem is a startup failure. Do not signal success merely because the user clicked Start.
- Keep the TLS key and owner credential material out of Git, issue comments, screenshots and diagnostics. A published invite should be treated as compromised if exposed.

## Troubleshooting decision table

| Symptom | Check | Safe response |
|---|---|---|
| Host cannot start | Data directory, bind/port collision, application error | Correct configuration and restart; preserve existing data |
| Owner bootstrap denied | Existing owner identity or remote request not loopback | Use owner sign-in and localhost owner setup |
| Invite refused | Wrong token, expiry, consumed invitation | Issue a fresh owner-authorized invite |
| Join remains pending | Owner has not decided | Review exact pending device; then approve/reject |
| Client rejects TLS | Server fingerprint differs from independently trusted fingerprint | Verify server identity out-of-band; never disable validation |
| Approved device loses access | Expired/revoked session or rotated device credential | Use supported renewal or deliberate new pairing |
| After restart user data is missing | Data directory or database identity changed | Stop host, identify original directory and restore complete backup |

## Current limits
This is a source-backed workflow document. It does not certify an end-to-end tested installation, network reachability, backup restoration or platform parity. Build, runtime verification and security acceptance were not performed as part of the docs-only work.
