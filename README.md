# INTER-LAN CROSS.OS COMMUNICATION TOOL

A native-first, owner-hosted LAN communications project using a .NET 10 HTTPS server, durable SQLite storage, Avalonia desktop clients, and a browser client for mobile/Android devices. **The project is under active development; merged source does not imply production or cross-platform acceptance.**

**Technical Authority:** Kirch Ivan Balite (@Kirch-Nairu)  
**Product plan:** [Issue #1](../../issues/1)  
**Current campaign:** [Issue #16](../../issues/16)  
**Live source of truth:** this repository's current `main` branch, not the historical P2 SHAs in older campaign documents.

## Integrated capabilities

| Surface | Implemented source on main | Important boundary |
|---|---|---|
| Owner HTTPS host | Kestrel/.NET 10; durable server data directory, server certificate and settings, first-run loopback bootstrap, authenticated owner controls, invitations and device approvals | Starting a server does not grant owner authority; only durable configured identity does |
| Identity and pairing | Invite → join request → owner decision → one-time exchange; revocable sessions and devices; native certificate fingerprint pinning | LAN advertisements are untrusted; never silently replace a pinned certificate |
| Direct messaging | Durable DMs and ordered history; cursor reads; send-idempotency; receipts, edits, replies and deletion contracts; SignalR delivery, typing and presence | Realtime is not durable proof; clients need cursor catch-up after reconnect |
| Groups (P3 authority) | Persistent group creation, metadata, OWNER/ADMIN/MEMBER membership, role-sensitive changes and paginated audit events; server HTTP routes | Group **message** persistence, group realtime fan-out and full attachment transport remain separate work |
| Desktop (Avalonia) | Owner start/stop and bootstrap UI, native pairing/client workspace, native direct messaging, group membership and administration surfaces | Linux/macOS durable native secret-store parity is not complete; platform behaviors remain unverified |
| Browser/mobile | Same-origin authenticated enrollment/session client, direct-message workspace, realtime ticket/reconnect and group management UI | Browser does not host the canonical owner server or provide native fingerprint pinning |

## Not yet accepted as a complete product

- Group messaging with durable history, strict membership-revocation cutoff and realtime group delivery ([Issue #35](../../issues/35)).
- LAN attachment/file-hosting lifecycle and its authorization, quotas and recovery.
- Telegram archival as a distinct archive tier, **not** realtime transport; credentials must remain server-side.
- Secure OS credential-vault pairing for native macOS/Linux ([Issue #40](../../issues/40)).
- Full runtime verification, portability checks, security review and release readiness.

For authoritative task status, use GitHub issues and PRs. Do not infer acceptance merely from a merged PR or a UI route.

## Run the local server

Requires .NET 10. The server project can be launched with:

```bash
dotnet run --project src/InterLan.Server/InterLan.Server.csproj
```

The default HTTPS listener port is `7443`, subject to persisted settings and explicit configuration. Configure a **durable, private data directory** before using real accounts: SQLite data and server TLS identity are stored beneath it. A regenerated certificate changes its fingerprint and a properly pinned native client must fail closed.

Desktop and web entry points are defined in their own project files. The browser must trust the server certificate through browser/OS HTTPS mechanisms; the application does **not** claim publicly trusted certificates or end-to-end encryption.

## Source-backed references

- [Engineering authority](AGENTS.md) and [handoff directory](00-HANDOFF/)
- [Kirch's current three-lane campaign](00-HANDOFF/INTERLAN_KIRCH_GOLD_MASS_CODE_CAMPAIGN.md)
- [Jane: backend/group work](00-HANDOFF/INTERLAN_JANE_BACKEND_MASS_DELIVERY.md)
- [Lucien: web/mobile work](00-HANDOFF/INTERLAN_LUCIEN_WEB_MASS_DELIVERY.md)
- [Nox: native desktop work](00-HANDOFF/INTERLAN_NOX_DESKTOP_MASS_DELIVERY.md)
- [Owner first-run and recovery guide](docs/operations/OWNER_SERVER_BOOTSTRAP.md)
- [P3 group HTTP/role contract](docs/api/GROUP_MEMBERSHIP_CONTRACT.md)
- [Realtime ticket and typing protocol](docs/api/REALTIME_CONNECTION_PROTOCOL.md)
- [Native certificate trust and pairing](docs/security/NATIVE_PAIRING_TRUST.md)
- [Browser enrollment and recovery](docs/web/SESSION_AND_ENROLLMENT_FLOW.md)

## Contribution and Git history

A source PR needs an actual scoped change and an associated issue. Workers retain their real Git commit authorship. For this campaign, Kirch-Nairu opens PRs through his authenticated account; co-author trailers are for genuine joint changes only. Normal merges preserve source history.

Temporary campaign direction: **code-first, no new tests or CI work**. This means build, test and runtime acceptance are **NOT RUN** unless separately evidenced. It does not relax authentication, TLS, server authority or secret-protection invariants. The campaign's long-term PR-count objective is not a claim that that many product changes already exist.
