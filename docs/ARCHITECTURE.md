# INTER-LAN Architecture — implemented runtime boundaries

**Authority:** the current source on `main`, not historical P0/P2 snapshots. This file describes implemented boundaries and distinguishes incomplete features. See [AGENTS.md](../AGENTS.md) and [issue #16](https://github.com/The-Kirion-Smithy/INTER-LAN-CROSS.OS-COMMUNICATION-TOOL/issues/16) for contributor governance.

## One canonical owner server

A single ASP.NET Core/Kestrel process manages the LAN communication domain. Its durable SQLite database owns the configured server identity, owner user, sessions, device approvals, conversations and membership records. Starting the process or using the desktop host does **not** make a connected user the owner.

- `src/InterLan.Server/InterLanServerHost.cs`: application startup, HTTPS binding, dependency injection, durable data location, middleware and route mapping.
- `src/InterLan.Server/InterLanServerHostOptions.cs`: optional data directory and explicit configuration overrides.
- `src/InterLan.Infrastructure/SqliteDatabase.cs`: WAL initialization, migration ledger and connection policies.
- `src/InterLan.Infrastructure/ServerCertificateManager.cs`: certificate/key lifecycle and SHA-256 fingerprint descriptor.
- `src/InterLan.Infrastructure/EnrollmentStore.cs`: durable identity, device approval and session state.

The default server path is `INTERLAN_DATA_DIR` if provided, otherwise the app-base `data` directory. SQL data, certificate and private key must stay within a protected, persistent owner-controlled directory. The server listens over HTTPS; client TLS trust must not be disabled for LAN convenience.

## Native and browser composition

| Runtime | Source | Authority |
|---|---|---|
| Embedded owner host | `src/InterLan.Desktop/OwnerServerRuntime.cs` / `OwnerPanel.cs` | Starts the **same** canonical owner-server implementation and uses real owner authentication |
| Native desktop client | `src/InterLan.Desktop/JoinPanel.cs`, `ChatWorkspace.cs`, `GroupWorkspace.cs` | Joins an owner host; does not become a second server |
| Shared native application services | `src/InterLan.Application/` | Certificate pinning, persisted pairing and session transport; never overrides server authorization |
| Android/mobile browser | `web-client/src/` | Same-origin authenticated browser, direct messaging, group membership UI, SignalR transport |
| Shared data contracts | `src/InterLan.Contracts/` | Wire request/response records and API version; clients consume, never invent privileges |

**Platform limit:** Native protected pairing storage has a Windows-backed path; equivalent OS-secured vault support for Linux/macOS remains work tracked in issue #40. Browser TLS trust comes from the browser/OS, not the native pinning adapter.

## Identity and enrollment path

1. Owner bootstraps the unconfigured server through the **loopback-only** owner setup endpoint. The persisted identity names the owner explicitly.
2. Authenticated owner issues a bounded invitation. New user/device submits a join request; the device remains pending until the owner decides.
3. The client exchanges an approved enrollment secret for session/device credentials. The server stores authentication material in protected/hashed representations; session/device revocation is checked against current server state.
4. Native client verifies an **independently confirmed** server certificate fingerprint before exchanging secrets and retains the verified pin for renewal. LAN discovery is a location hint only, not evidence of server identity.
5. An authenticated client can access **only** resources for which current server-side membership and session rules permit it.

Source boundaries: `EnrollmentEndpointMappings.cs`, `AuthorizationHelpers.cs`, `EnrollmentStore.cs`, `ClientPairingStateStore.cs`, `NativePinnedHttp.cs` and `PinnedTransportFactory.cs`.

## Direct messaging and realtime events

The existing direct-message domain persists messages and receipts and exposes cursor paging and per-user conversation preferences. The **server** validates that a caller belongs to a direct conversation and enforces stable client message IDs for idempotency. Browser and native clients render persisted history; neither should manufacture delivery acknowledgments.

SignalR transport is a separate, ephemeral channel:
- `/api/v1/realtime/ticket` issues short-lived, one-use tickets for browser WebSocket connection.
- `Realtime/ChatHub.cs` resolves a durable session principal, registers connections and publishes to authenticated recipient user groups.
- `TypingDispatchGate` is **process-local**, bounded and non-durable; typing indicators are not message history.
- Clients need cursor-based catch-up after disconnect/reconnect; the mere arrival of an event does not prove persisted history was read.

See [realtime protocol](api/REALTIME_CONNECTION_PROTOCOL.md).

## P3 group authority is not P3 group messaging

Group creation, metadata, OWNER/ADMIN/MEMBER membership, role mutation and paginated audit events are implemented through `GroupStore.cs`, `GroupEndpointMappings.cs`, `GroupRolePolicy.cs`, `GroupTextPolicy.cs` and migration `008_p3_group_authority.sql`. Group role checks are distinct from server account roles. Owner/admin UI controls are convenience, **not** authorization.

What is **not** established by these routes alone: durable group message history, reliable group realtime fan-out after membership changes, group receipts, and group attachment transport. These remain issue-linked work, including [#35](https://github.com/The-Kirion-Smithy/INTER-LAN-CROSS.OS-COMMUNICATION-TOOL/issues/35).

## Files and Telegram remain separate domains

The initial SQLite schema includes `attachments`, `archive_jobs` and `telegram_archives` tables. Schema presence does **not** establish a finished upload/download, retry, retention, encryption or archive execution service. Telegram is a future archive tier; server-side secrets must never reach browser/native code. Server can read message payloads, therefore **do not claim end-to-end encryption**.

## Configuration, verification and unresolved work

`ServerRuntimeSettingsResolver` combines persisted bind address/port/discovery/approval/storage settings with explicitly supplied runtime configuration. A persisted setting can differ from the active listener until restart; admin UI must report this distinction accurately.

Operational documents: [owner setup](operations/OWNER_SERVER_BOOTSTRAP.md), [group contract](api/GROUP_MEMBERSHIP_CONTRACT.md), [native certificate trust](security/NATIVE_PAIRING_TRUST.md), [database model](DATABASE.md).

**Status discipline:** A source merge is not proof that native Windows/Linux/macOS, browser, recovery or threat-model verification passed. Where no test/build evidence exists, report **NOT RUN**. The current technical authority requested no new tests/CI effort in the code-first wave; this does not waive future acceptance work.
