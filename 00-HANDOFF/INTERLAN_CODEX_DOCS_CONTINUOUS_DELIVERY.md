# INTER-LAN — CODEX DOCUMENTATION-ONLY CONTINUOUS DELIVERY

**Directive:** Kirch-owned documentation execution campaign, 2026-10-19. This file is an **opt-in docs-only work order**. It does not mean a Codex session has been launched.

- **Repository:** `The-Kirion-Smithy/INTER-LAN-CROSS.OS-COMMUNICATION-TOOL`
- **Technical authority and required qualifying PR creator:** `Kirch-Nairu`
- **Campaign tracker:** [issue #16](https://github.com/The-Kirion-Smithy/INTER-LAN-CROSS.OS-COMMUNICATION-TOOL/issues/16)
- **Handoff issue:** [#102](https://github.com/The-Kirion-Smithy/INTER-LAN-CROSS.OS-COMMUNICATION-TOOL/issues/102)
- **Docs-only starting checkpoint:** `main@f1acb6d9759cd4bf05dda603335d6ba2e4b81be9`, global GitHub merged-author count **177**, of which **43** merged PRs in this repository. These are **historical snapshots**; query actual GitHub state on every batch.
- **User-requested volume:** **850 further documentation PRs**, as a long-term ambition, only if 850 *different, independently useful* documentation improvements can actually be established. At the snapshot, 847 more globally authored merges would reach the prior 1,024 Pull Shark count target; 850 further would exceed that by three. This is **not** permission to invent PRs, pad prose, or assert a badge was earned. Report both counts separately and stop once the campaign's requested meaningful output is reached or genuine work is exhausted.

## START COMMAND — DOCUMENT, COMMIT, SUBMIT, REPEAT

You are **Codex**, executing a high-throughput **DOCUMENTATION-ONLY** GitHub campaign. Your work product is not a strategy deck and not a list of hypothetical PRs. Read source, identify one real documentation deficiency, **write the actual Markdown**, issue it, commit it and deliver a reviewed, genuine PR. Then immediately identify the next unrelated issue and repeat for the duration of the **active Codex session**, with up to three distinct nonoverlapping docs issues in flight. Never promise background work or claim worker sessions started by assigning GitHub issues.

This doc-only order supersedes the code-first task selection in `INTERLAN_CODEX_HIGH_VELOCITY_HANDOFF.md` **only when this directive is explicitly selected**; its code, security, history, attribution and truthful-verification invariants remain binding. Do not edit implementation files, SQLite migrations, tests, GitHub Actions, generated source, or live settings merely to create documentation PRs. If doc audit uncovers a code defect, open a source-backed engineering issue and **document the limitation accurately**; do not silently fix it in this docs-only lane.

### Required initial reconnaissance

1. Fetch **live** `main` SHA and author identity; read `AGENTS.md`, `00-HANDOFF/INTERLAN_INVARIANTS.md`, `00-HANDOFF/INTERLAN_EXECUTION_STATE.md`, this order, open issues/PRs, latest merged PRs, docs tree and actual current code.
2. Query GitHub for the global total using `is:pr is:merged author:Kirch-Nairu` and the repository-local subset. Record counts and query time. Do not conflate total PRs merged by that user with platform badge eligibility.
3. Build a **working, ephemeral source-backed gap inventory** from actual source-to-document coverage, not 850 prewritten tickets. Prioritize API/authorization documentation that developers need to build clients safely and operator recovery content where a mistaken command could lose data.
4. Check whether another documentation PR already covers each exact issue. Explicitly compare latest main and active branches. If the material already exists, choose a different deficiency.

## ACCEPTANCE GATE FOR EVERY DOCUMENTATION PR

**All** of the following must hold; otherwise do not create the PR:

- **Independent engineering value:** a distinct use case, API contract, operator procedure, threat boundary, architecture rationale, migration behavior or troubleshooting decision tree. Cosmetic rewording, splitting a paragraph into multiple commits, repetitive README restatements and fabricated screenshots are not distinct documentation work.
- **Current-source grounding:** identify the exact applicable code file(s), endpoint route(s), data tables, environment settings or commit(s). Explain the observed source behavior; mark anything planned or unimplemented separately. No invented routes, option names, runtime guarantees, version numbers or logs.
- **Concrete usable content:** one coherent doc or a necessary amendment to existing doc with examples, preconditions, expected behavior, failure/rollback semantics, cross-platform distinctions and a boundary of what it **does not** prove when appropriate. Match depth to the issue.
- **No duplicates:** existing docs, AGENTS, handoffs, issue discussion and open PRs must not already answer the same operational question adequately. Extend canonical docs rather than spawning contradictory duplicates.
- **One real issue and branch:** issue states the precise documentation gap, reader, observable outcome, source evidence, paths and exclusion. Branch starts from live main; one logical issue per PR.
- **Truthful contributor identity:** commits preserve the actual author. A PR qualifies for Kirch-Nairu's authored count only if authenticated GitHub reports its creator as **Kirch-Nairu**. A bot-created PR must not be misrepresented as Kirch's; return a ready branch if Codex lacks Kirch's authentication. No synthetic coauthorship.
- **Real review and merge:** inspect exact diff, seek independent nonauthor review for sensitive or authoritative docs, distinguish review request from approval, use a normal merge commit to preserve history and require GitHub's actual `merged=true` and integration SHA. Refuse conflicts and stale source.
- **Verification honesty:** reading source is not runtime execution. Report build/tests/CI **NOT RUN** if not run; do not import old PASS results. Never call documentation instructions production-verified without corresponding evidence.

## DOCS-ONLY ISSUE DISCOVERY DOMAINS

Each example is a *candidate area*, not an authorization to generate a pre-canned PR without discovering a real source-backed hole.

| Domain | Read current source / evidence | Suitable independent user need |
|---|---|---|
| Identity and enrollment | `src/InterLan.Server/EnrollmentEndpointMappings.cs`, `src/InterLan.Infrastructure/EnrollmentStore.cs` | Approval lifecycle, token/session expiry, revoked-device UX and exact denial semantics |
| Direct messaging | `src/InterLan.Server/MessagingEndpointMappings.cs`, `src/InterLan.Infrastructure/ChatStore.cs` | Endpoint request/response, idempotency and conflict semantics, cursor and receipt behavior |
| Group authority | `src/InterLan.Server/GroupEndpointMappings.cs`, `src/InterLan.Infrastructure/GroupStore.cs`, `docs/api/GROUP_MEMBERSHIP_CONTRACT.md` | Role matrix, event cursors, membership removal and permissions without claiming group messaging |
| SignalR and reconnect | `src/InterLan.Server/Realtime/ChatHub.cs`, `src/InterLan.Server/Realtime/RealtimeTicketStore.cs`, `web-client/src/realtime/connection.js` | Single-use ticket lifecycle, reconnect states, rate limiting and revoke behavior |
| Native trust | `src/InterLan.Application/ClientPairingStateStore.cs`, `src/InterLan.Desktop/JoinPanel.cs`, `docs/security/NATIVE_PAIRING_TRUST.md` | Fingerprint confirmation, Windows DPAPI limitations, safe restore/unpair; Linux/macOS vault issue #40 |
| Canonical server and networking | `src/InterLan.Server/InterLanServerHost.cs`, `src/InterLan.Server/Networking/LanDiscoveryBroadcaster.cs`, `src/InterLan.Infrastructure/ServerCertificateManager.cs` | Local discovery trust, TLS identity restore, SAN changes and bootstrap boundaries |
| SQLite and settings | `src/InterLan.Infrastructure/SqliteDatabase.cs`, migration files, `src/InterLan.Application/RuntimeConfigurationStore.cs` | Data invariants, backup/restore ordering, WAL semantics, migration compatibility and configuration precedence |
| Browser client | `web-client/src/Workspace.jsx`, `web-client/src/api/client.js`, `web-client/src/GroupWorkspace.jsx` | Real delivered workflow, troubleshooting expired sessions, accessible mobile behavior |
| Desktop client | `src/InterLan.Desktop`, `src/InterLan.Application` | Owner/join runtime, known unsupported platforms, manual troubleshooting and role-appropriate procedures |
| Future services | Issue #35 group messaging, #38 attachment contract, #40 native vault, archive architecture | **Explicitly proposed/unimplemented** interface or decision docs only. Never describe as shipped. |

Other worthwhile categories include release operation rollback procedures, Windows/Linux/macOS platform support matrix, developer onboarding, log redaction, safe incident triage, concurrency assumptions, request examples against **existing** APIs, schema upgrade recovery, offline-first architecture, observability boundaries and precise error handling. The candidate must be evidenced by current sources and not duplicate `docs/ARCHITECTURE.md`, `docs/DATABASE.md` or existing `docs/operations` guides.

## LOOP — MAXIMUM USEFUL DOCS THROUGHPUT

1. **Count:** refresh global and repository-local merged-PR totals; record prior batch results.
2. **Audit:** use searches, code references and documentation references together; select up to three independent substantive missing documents, ideally in API, operator and client domains. If three cannot be justified, do fewer.
3. **Issue and branch:** one source-backed issue and one fresh-main branch per document. No 850 precreated branches or fake queue.
4. **Draft:** write complete, reader-tested Markdown directly in `docs/api`, `docs/operations`, `docs/security`, `docs/web`, a new scoped documentation directory, or existing canonical doc; preserve the source of truth and no contradictory claims.
5. **Review exact diff:** check technical accuracy against current source, links, examples, safety, redaction and overlap. Correct inaccuracies before PR.
6. **Submit:** PR creator must be authenticated `Kirch-Nairu` for intended authored count. Include issue link, source file references, readme navigation if genuinely needed, explicit test/run status. Request genuine nonauthor review.
7. **Merge conservatively:** normal merge when safe, capture PR number, author, HEAD and merge SHA, and confirm `merged=true`. Do not auto-merge a documentation claim about authentication, cryptography, data deletion or restore without sufficient independent technical review.
8. **Log batch:** comment on #16 with source issue links, actual doc paths, source citations, confirmed PR/merge status, checked global count and remaining toward 850 additional documentation PRs and the previous 1,024 count target.
9. **Repeat:** discover further real documentation gaps from *updated main*. Continue during the active execution session. Do not end solely because the first three are merged.

### Accounting ledger after each wave

`Batch: N | Starting main: SHA | Ending main: SHA | Actual GitHub author: login | Docs PRs merged in this dedicated initiative: K/850 | Global Kirch merged-author count: G | Additional globally to 1,024: max(0, 1024-G) | Issue/PR/HEAD/merge SHA/path/source refs: ... | Review state: ... | Build/tests/CI: NOT RUN or exact evidence | Next evidence-backed gaps: ...`

Do not claim 850 completed until **850 actual new distinct docs PRs have been merged and individually confirmed** since this initiative began. If the upstream repository makes a requested document obsolete mid-batch, abandon/rewrite that candidate rather than merging misleading docs.

### Stop / escalation rules

Stop opening new PRs when 850 *meaningful* documentation PRs have actually been integrated, the active session ends, Github permissions/creator identity fail, a security- or data-critical documentation claim cannot be reviewed, or further nonduplicative meaningful work cannot be established. Do **not** reach 850 by repeating paraphrases, blank PRs, arbitrary word splits or author impersonation. A short credible series is better than a fraudulent 850 count. Stop does not imply any background work continues.

## EXACT CODEX START MESSAGE

> Open The-Kirion-Smithy/INTER-LAN-CROSS.OS-COMMUNICATION-TOOL on LIVE main. Read AGENTS.md, 00-HANDOFF/INTERLAN_CODEX_DOCS_CONTINUOUS_DELIVERY.md, INVARIANTS and #16. I authorize a documentation-only, source-grounded delivery campaign aiming for **850 further genuine merged documentation PRs**, not filler PRs. Immediately inspect current source versus existing documentation, pick up to three distinct missing technical/operator docs, create grounded issues and fresh-main branches, write and review actual Markdown, submit through authenticated Kirch-Nairu only if that is your real login, merge normally when supported and record evidence plus refreshed counts. Repeat meaningful iterations for this entire active Codex session. No code changes, no local AI dependency, no repetitive cosmetic work, no forged author/review/merge records, and never claim unverified runtime behavior. Execute, do not just plan.
