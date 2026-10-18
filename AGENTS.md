# INTER-LAN Engineering Authority

Technical Authority: **Kirch Ivan Balite**

Active implementation phase: **P2 — Direct Messaging + Durable Pairing**

Accepted predecessor:

`main@91c010dd3bd448fc9bd5ec9159e94262569e3e79`

Active branch:

`KIRCH-INTERLAN-P2-DIRECT-MESSAGING`

## Execution authority

Read and obey:

1. `00-HANDOFF/INTERLAN_V1_CODE_CAMPAIGN.md`
2. `00-HANDOFF/INTERLAN_EXECUTION_STATE.md`
3. `00-HANDOFF/INTERLAN_COMMIT_LEDGER.md`
4. `00-HANDOFF/INTERLAN_INVARIANTS.md`
5. `00-HANDOFF/INTERLAN_KNOWN_RISKS.md`
6. `00-HANDOFF/INTERLAN_FINAL_CICD_CONTRACT.md`

Where older phase wording conflicts with these active campaign documents, the active campaign documents govern.

## Core laws

- The server owner is durable configured authority. It is never inferred from which client is currently connected.
- Native desktop and web clients share the same server-side authorization contract.
- Android is a web client in V1 and cannot host the canonical server.
- Server-only secrets, including Telegram credentials and the server TLS private key, never go to clients.
- Telegram is archive tier, not realtime transport.
- Discovery is untrusted information only.
- Invite/session/device credentials are persisted server-side only as cryptographic hashes.
- Native clients pin the server certificate fingerprint after explicit pairing.
- Do not claim E2E encryption while the server can read message payloads.
- Security, authorization, persistence, and migration regressions stop forward work.
- P2–P7 target 300+ meaningful atomic commits per phase. No fake commit farming.
- P2 forward uses normal merges; do not squash accepted phase history.
- New CI/CD architecture is deferred until P7 code freeze. Local executable verification is not deferred.


## CURRENT THREE-LANE KIRION SMITHY CODE-FIRST DISPATCH — 2026-10-19

The older P2 branch/SHA declarations at the top of this file are historical snapshots, not live HEAD. Before source edits, read actual GitHub main and compare any branch against it. The latest verified launch source for this dispatch is main@679ee48de3f6989638b013dadc4b57d7a7b68101, after issue #21/PR #23 and issue #22/PR #24 merged. Future merges supersede this SHA; do not pin fresh branches to it when main has advanced.

Repository-local launch documents, ordered:

1. 00-HANDOFF/INTERLAN_KIRCH_GOLD_MASS_CODE_CAMPAIGN.md — account attribution, code-first campaign, issue/PR/merge truth and integration.
2. 00-HANDOFF/INTERLAN_JANE_BACKEND_MASS_DELIVERY.md — Jane's server/realtime/group/files/archive lane (#17, #20).
3. 00-HANDOFF/INTERLAN_LUCIEN_WEB_MASS_DELIVERY.md — Lucien's Android/browser client lane (#18).
4. 00-HANDOFF/INTERLAN_NOX_DESKTOP_MASS_DELIVERY.md — Nox's Avalonia/native client/owner lane (#19).

Issue #16 remains overall campaign tracking; issue #25 is the handoff publication unit. Kurt is not one of these three execution lanes.

**Temporary code-first scheduling:** The Technical Authority requested no new tests/CI work or test execution during this sprint. Record verification NOT RUN and do not claim CI, security or release acceptance. This is a work-order change only: it does not repeal the core identity, revocation, server-authority, persistence, secret-handling, migration or truthful-attribution invariants listed above. Existing future phase acceptance obligations remain unresolved until genuinely verified.

**PR credit routing:** Kirch-Nairu genuinely opens PRs intended for his Pull Shark count using his authenticated account. Workers' actual source commits must not impersonate Kirch; a Kirch co-author trailer is appropriate only for an actual jointly authored commit. Kirch cannot approve a Kirch-authored PR; use independent nonauthor review and record the actual review state rather than inventing an approval.

**No manufactured throughput:** one material issue per PR, meaningful source changes and natural atomic commits, normal merge preserving history, actual GitHub merged=true required. Do not create dummy PRs or empty commits for the 1,300-PR ambition.
