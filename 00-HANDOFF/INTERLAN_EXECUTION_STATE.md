# INTER-LAN EXECUTION STATE

## LIVE RECOVERY POINTER — supersedes historical "Current authority" below (2026-10-19)

**Repository:** `The-Kirion-Smithy/INTER-LAN-CROSS.OS-COMMUNICATION-TOOL`. **Technical Authority:** Kirch Ivan Balite / `Kirch-Nairu`. **Known integration checkpoint:** `main@d8dc5d600e9948eee1ca02ea0302f7c311e40d6d` after merged PR #79; **always re-resolve current main** before continuing.

**Accepted implementation slices:** direct-message transport/persistence, server owner bootstrap and session/device authority; owner/browser/native UI; realtime typing gate (#28); P3 group member and role authority with migration 008 and audit routes (#33); authenticated browser DM (#29) and group management (#43); native owner/pairing/DM (#32) and group management (#58); server TLS partial-key fail-closed handling (#76).

**Outstanding or unverified:** group message storage/realtime/strict removal cutoff (issue #35); file hosting/Telegram archival runtime; macOS/Linux native credential vaults (#40); comprehensive build/runtime/security acceptance for this fast code-first wave (**NOT RUN**). Current operations docs cover WAL recovery and TLS identity, but no actual restore test is asserted. `scripts/Build-InterLanReadyQueue.ps1` can identify only new unmerged issue-backed branch diffs; an empty queue is **not** evidence of no engineering backlog.

**Precedence for continuation:** live GitHub main and actual source > current AGENTS/invariants and accepted PRs > current issue-specific handoff > archived P2/C075/C100 snapshots. Preserve the prior C075 PASS result *as a historical snapshot only*; do not transfer it to current integrated main.

---


This file is the compact recovery point for continuing the campaign after context loss.

## Current authority

Repository:
`Kirch-Nairu/INTER-LAN-CROSS.OS-COMMUNICATION-TOOL`

Technical Authority:
**Kirch Ivan Balite**

Accepted main:
`91c010dd3bd448fc9bd5ec9159e94262569e3e79`

Active phase:
**P2 — Direct Messaging + Durable Pairing**

Active branch:
`KIRCH-INTERLAN-P2-DIRECT-MESSAGING`

C075 checkpoint source head:
`926ffffdbc437a85ebb1c9109ee85acc2a524383`

P2 issue:
`#6 — Direct messaging, realtime delivery, history and reconnect`

P2 PR:
`#7 — P2: direct messaging, realtime delivery and persistent pairing`

P3 issue:
`#8 — Group chat, membership authority and realtime group delivery`

## Proven before current code wave

- persistent TLS certificate and Windows-compatible private-key loading;
- bootstrap/owner authority;
- invite/join/approval;
- token hashing;
- approved-device persistence;
- pairing-state persistence;
- session renewal without re-enrollment;
- canonical DMs;
- DM IDOR rejection;
- durable ordered messages;
- duplicate suppression;
- SignalR delivery;
- offline cursor catch-up;
- message history survives restart.

## Implemented in current P2 hardening wave

- SQLite WAL + busy-timeout baseline;
- per-client rate-limit partition keys;
- realtime connection registry keyed by session/device;
- active realtime abort on device/session revocation;
- recipient-only delivered/read receipt semantics;
- receipt query API;
- multi-device attachment to one durable user identity;
- device credential lifecycle timestamps;
- device credential rotation;
- rotation revokes prior device sessions/credential;
- client-side credential rotation persistence;
- canonical server runtime-settings contract;
- SQLite-backed server settings store;
- explicit configuration-override precedence;
- Kestrel/bootstrap/discovery use resolved settings;
- system/enrollment/messaging endpoint extraction;
- reusable in-process `InterLanServerHost`;
- reusable `InterLanServerInstance` lifecycle;
- desktop `OwnerServerRuntime` integration spine;
- all active verification projects included in `InterLan.sln`;
- shared `InterLan.Testing` library;
- shared repository/artifact discovery;
- shared loopback HTTPS client;
- resilient retrying server-process harness;
- P2 realtime smoke migrated to the shared server harness;
- native realtime test transport now prefers Authorization header over query token;
- framework request-start logging suppressed to reduce browser access-token log exposure.

## C075 checkpoint result

**PASS — operator-local Windows replay at `3a1805fb7c985b83ca72bd2c6f4353abcc435f1e`.**

Passed:
1. `dotnet build InterLan.sln -c Release`
2. `InterLan.P1Checks`
3. `InterLan.P1NetworkSmoke`
4. `InterLan.P2Checks`
5. `InterLan.P2RealtimeSmoke`

The replay proved the current pairing, credential rotation, multi-device identity, SQLite WAL policy, recipient receipt semantics, DM persistence, realtime delivery, offline catch-up, and immediate revocation paths on Windows.

Forward implementation is authorized toward the C100 architecture checkpoint.

## C100 architecture checkpoint

C100 source head before this review:

`bc56986096f4e40690f8bb35ebfb3d7666363450`

The C075→C100 wave implemented:
- P1 network smoke migration to the shared server-process harness;
- concurrent canonical DM creation checks;
- concurrent unique-send durability checks;
- concurrent duplicate-send idempotency checks;
- conflicting idempotency-key replay rejection;
- rate-limit partition isolation checks;
- historical schema fixture support;
- migration upgrade checks through current schema;
- desktop in-process owner-host lifecycle checks;
- aggregate P0–P2 local suite runner.

**C100 local replay is required before the next large wave.**

After C100 passes, remaining P2 work includes:
- deeper SQLite contention/torture beyond the current concurrency checks;
- end-to-end rate-limit behavior rather than partition-key unit proof only;
- browser realtime credential policy;
- mutable owner settings with explicit restart semantics;
- migration data-preservation fixtures, not schema-only upgrade proof;
- secure-storage platform adapters for macOS/Linux;
- typed client SDK expansion beyond pairing/session restore;
- DM pagination/conversation projections/unread state;
- continued correctness hardening toward C125/C150.

## Deferred

- new GitHub Actions architecture;
- new phase workflows;
- deployment automation;
- release publishing;
- signing/notarization automation;
- branch-protection engineering.

Existing workflows may run as passive portability signals only.
