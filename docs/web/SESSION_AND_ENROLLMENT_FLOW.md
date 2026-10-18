# Browser enrollment, session and recovery flow

**Sources:** `web-client/src/main.jsx`, `src/serverInfo.js`, `src/auth/enrollment.js`, `src/api/client.js`, `src/realtime/connection.js`, `Workspace.jsx`; `src/InterLan.Server/EnrollmentEndpointMappings.cs` and `MessagingEndpointMappings.cs`.

## Scope
INTER-LAN V1 Android support is a browser client connected to the owner-managed LAN server. Browsers do not host canonical server authority. Browser traffic must use the authenticated HTTPS origin serving the web application. A browser cannot reproduce the native certificate pinning mechanism; the HTTPS connection must be trusted using the browser/OS certificate-validation path.

## The first device request
1. Browse to the owner server's trusted HTTPS address. The shell fetches `GET /api/v1/server/info` and renders configured/offline state.
2. Obtain an invite from the authenticated owner; each invite is meant for one joining request and expires.
3. Enter username, display name and device name. A secure browser context provides cryptographically strong randomness for a one-time `enrollmentSecret`.
4. Submit `POST /api/v1/join` with the owner-issued invite and device details. The server returns the request ID and pending state; this is not yet authentication.
5. Owner reviews `GET /api/v1/join/pending` and decides via `POST /api/v1/join/{requestId}/decision`.
6. When approved, exchange `POST /api/v1/join/exchange` using request ID and original one-time enrollment secret. The server returns a session bearer and device identity only after validation.
7. Enter the authenticated browser workspace; fetch real directory, conversations and history from the owner server.

## Sensitive material handling
The current browser architecture is memory-session oriented; the enrollment secret and bearer must not be copied into URL fragments, arbitrary query parameters, browser logs or localStorage. Same-origin `api/client.js` permits requests only under `/api/v1/` on the active origin. It attaches bearer only to those API calls. A one-time SignalR ticket, not the long-lived bearer, is used in the WebSocket query string.

The server exchange contract also provides a device credential for device-bound renewal, but a browser UI must not assert durable cross-reload pairing unless a defensible protected storage/renewal design is actually implemented and accepted. Reloading may require re-entry, and unsent drafts may disappear.

## Expiry and reconnection
- Use returned `expiresUtc` to present the time remaining and end the workspace when the current session is invalid.
- A 401/403 from authenticated API should clear or suspend access according to actual session policy. Do not display cached conversation contents from a revoked account as current authoritative data.
- On network loss, keep the user informed without claiming that outgoing messages were persisted. Use original `clientMessageId` for retry of the same logical message where the server contract supports idempotency.
- On SignalR reconnect, obtain a fresh one-time ticket, then fetch missed direct history by cursor; do not assume real-time transport guarantees delivery.
- On explicit logout, request `POST /api/v1/auth/logout`, clear local bearer and stop realtime handlers. If server revocation could not be confirmed due to an outage, report that accurately.

## Owner/device recovery
Owner may revoke a session or device. Revoked credentials cannot be treated as ordinary connection-timeout problems. Direct the user to contact the owner for approval rather than infinite retry loops. A new invite and deliberate enrollment may be required.

## Operator troubleshooting
| Problem | Likely cause | Safe action |
|---|---|---|
| Server status offline | LAN DNS/route/host down or untrusted HTTPS | Verify correct trusted origin and owner host availability |
| Join rejected | Invalid/consumed/expired invite | Obtain new invite |
| Approval still pending | Owner has not accepted the device | Ask owner to review the request |
| Exchange rejects 401 | Not approved, rejected or invalid secret | Verify decision; re-enroll if consumed/invalid |
| Realtime repeatedly reconnects | Network issue, invalid session, expired single-use ticket | Fetch new ticket or terminate unauthorized session |
| Browser reload loses session | Deliberate memory-session boundary | Reauthenticate; do not pretend persistence exists |
| User cannot access group | Not currently an authorized member | Owner/admin grants access through group membership endpoint |

## Acceptance status
This is a code-backed usage/developer note, **not** evidence of a verified production browser journey. No build, integration tests or CI evidence are claimed for this documentation change.
