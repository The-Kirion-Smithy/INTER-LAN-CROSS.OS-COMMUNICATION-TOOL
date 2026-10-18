# KIRION SMITHY — INTER-LAN 1300-PR CODE-FIRST COMMAND AUTHORITY

**Issued by and GitHub author:** Kirch Ivan Balite / @Kirch-Nairu
**Repository:** The-Kirion-Smithy/INTER-LAN-CROSS.OS-COMMUNICATION-TOOL
**Master tracking:** Issue #16
**Handoff publication issue:** #25
**Dispatches:** Jane / backend; Lucien / browser; Nox / native desktop
**Kurt:** excluded from this campaign
**Snapshot at issue:** main@679ee48de3f6989638b013dadc4b57d7a7b68101 (October 19, 2026)
**Status:** LIVE HANDOFF — instructions, not evidence that another autonomous session started

## 0. Mission

Accelerate actual INTER-LAN source development across three disjoint implementation lanes, generate a high volume of useful atomic code commits, maintain one issue per PR, and have Kirch-Nairu genuinely open eligible PRs through his connected GitHub account. The ambition is 1,300 merged, meaningful PRs over the complete campaign. It is an ambition, not an instruction to manufacture commits or mark uncompleted work as complete. A session may end with only a few legitimately finished PRs.

This is **CODE FIRST** by Kirch's express direction: do not build new GitHub Actions workflows, release automation, CI architecture, test projects, test-only PRs, or spend the sprint running full suites. Record build/test state as NOT RUN; never label it PASS. Do not knowingly introduce a broken contract or security bypass merely to increase speed. No release/stable/security-verified claim is authorized from code-only evidence.

The three workers may invent product features, implementation approaches, API shapes within their module responsibility, refactors, UI composition, data structures, and whole subsystems. They need not request approval for each implementation idea. The limit is cross-lane ownership, destructive changes, data loss, compromised identity/authorization/secrets, and conflicting interfaces. Unrestricted creativity does not mean silent destructive writes to main.

## 1. Absolute source-of-truth order

1. Live GitHub main branch SHA at the instant a new task starts.
2. Current actual source files, route contracts, migrations, and current open PR diffs.
3. Existing AGENTS.md, 00-HANDOFF/INTERLAN_INVARIANTS.md, and phase architecture.
4. This current code-first handoff and worker-specific child handoff.
5. Historical progress summaries as hints only.

Do not trust the older README statement that P2 is still the sole active branch. Current main already merged PR #23 (browser enrollment; issue #21) and PR #24 (desktop owner start/stop; issue #22). Main after these changes is 679ee48de3f6989638b013dadc4b57d7a7b68101. Re-resolve main before every subsequent branch or PR.

Open PR #9 remains a draft against a non-main rebaseline branch; do not blindly merge it into main. P3 branch KIRCH-INTERLAN-P3-GROUP-CHAT was seen 98 ahead and 31 behind main at audit; preserve its useful work but reconcile against current identity, sessions, ticket handling, and message persistence. Never reset, force-push, or destructively rewrite its old work to simplify bookkeeping.

## 2. Three distinct execution lanes

### Jane — backend/domain/infrastructure and integrated server authority

Own server routes, shared contracts, migrations, SQLite repositories, SignalR, P3 groups, P4 files, P5 archive, systemic persistence/IDOR concerns. Own issues #17 and #20. No modifying web-client or Avalonia UI without opening a cross-lane dependency and receiving file ownership transfer.

### Lucien — browser and Android-responsive client

Own web-client/ JavaScript/React, enrollment/approval continuation, local session policy, DM/group conversation UI, file UI, reconnect, notifications, accessibility. Own issue #18; issues #21/#23 are already merged predecessors. No touching server privilege code or Avalonia without dependency handoff.

### Nox — native Avalonia and client composition

Own src/InterLan.Desktop, owner wizard, native client pairing/session UI, native DM/groups/files UX, settings, runtime/publish composition proposals. Own issue #19; issue #22/PR #24 is a merged predecessor. No web-client changes or edits to Jane's server/migrations without dependency handoff.

Each worker should be independently creative within lane and propose more useful features. If a new feature touches another worker's path, create a dependency issue and expose the needed interface; do not overwrite the other's changes. Different branches must start from the latest main; no common mutable worker branch.

## 3. The unit of genuine, countable engineering work

Every qualifying unit is:

1. A concrete, unique GitHub issue that defines the problem and changed behavior (created by Kirch via his account when Kirch-owned issue authorship is desired).
2. Worker START comment containing issue ID, initial main SHA, branch, proposed files, dependency IDs and precise intended behavior.
3. Exactly one isolated feature/fix branch per implementation issue. Use names such as KIRCH-INTERLAN-JANE-GRP-001-ROLE-AUTHORITY, KIRCH-INTERLAN-LUCIEN-WEB-001-DM-INBOX, and KIRCH-INTERLAN-NOX-NATIVE-001-OWNER-SETUP.
4. Real source changes and atomic commits. For a substantive change, a series of natural domain/contract/persistence/API/client commits is valid. No empty commits, whitespace farming, random rename churn, copy-paste filler, or trivially dividing one edit simply for multiple counters.
5. Worker submits a READY-to-PR return with exact commit HEAD and list of changes. Any omitted or unrun verification is marked NOT RUN.
6. Kirch inspects the proposed diff, can make substantive integration/contract changes under his account, and **opens the PR through the authenticated Kirch-Nairu connector**. A worker opening their own PR does not count as Kirch's Pull Shark PR and should not be represented otherwise.
7. PR includes a Closes #ISSUE link, source HEAD SHA, behavior/diff summary, what was NOT RUN, and known unfinished edges. Kirch requests review from another person/account, not from Kirch himself on a Kirch-authored PR.
8. If merge conflicts or known high-impact breakage exist, fix code instead of merging blindly. Merge with normal merge commits to preserve meaningful commit history; do not squash prior accepted branch history.
9. Capture actual PR number, PR author, exact merge SHA, latest main SHA, issue closure, and count from GitHub after the merge. Update #16 ledger.

No PR exists merely because a branch was created. A PR is not merged until GitHub returns merged=true. A reviewer request is not an approval. A passed historical workflow is not current proof. GitHub search count is not proof of badge eligibility.

## 4. Authorship and credit contract

**This is a Kirch-owned PR campaign.** Every PR intended for Kirch's Pull Shark must genuinely be opened by @Kirch-Nairu. Kirch may also author issue specs, architecture, integration fixes, contract changes, and actual merge commits. For each worker-authored source commit, retain its real Git author and committer. Do not impersonate Kirch in worker git configuration.

If Kirch materially contributed to the specific commit, worker and Kirch can truthfully add:

    Co-authored-by: Kirch Ivan Balite <291564617+Kirch-Nairu@users.noreply.github.com>

Do not append this trailer where Kirch did not actually co-author that commit. Kirch's technical authority or later PR opening alone is not a co-authored source change. Conversely, Kirch can make an actual source commit under his account during integration, with genuine Kirch authorship.

GitHub PRs have a creator/author, reviewer, and merger, not a PR-level coauthor field. No account sharing, no worker impersonation, no fabricated review records, no self-review. Route Kirch-authored PRs to an independent worker for review. If user-directed sprint speed requires merging before review/verification, record that transparently rather than pretending that a requested review was approved.

## 5. Issue schema that Kirch will create or authorize

Title: [LANE][MODULE][NNN] Verb + exact behavior
Body:
- Parent: #16 and lane parent (#17/#18/#19)
- Baseline SHA / target files
- Current broken/missing behavior and source evidence
- Intended change and observable behavior
- Explicit excluded scope
- Cross-lane dependencies, contract details and migration number if needed
- Security/authorization/persistence invariants affected
- Source acceptance: changed behavior, observable API/UI path, linked commit(s)
- Verification: NOT RUN BY INSTRUCTION, any actual evidence separately
- Risk/disposition and assigned worker

Issues may be drafted in parallel by workers as comments on their parent. Kirch creates or authorizes child issues before any corresponding PR opens. Duplicated issue content or multiple PRs from one artificial triviality does not count as useful progress.

## 6. Atomic commit rules — many real commits, no manufactured history

Valid adjacent commits on one real feature can be:
- input/domain type or contract;
- validation and error handling;
- schema/migration and rollback-safe DDL;
- repository storage and query;
- transaction/idempotency/concurrency;
- endpoint, auth check or realtime event;
- client state and transport;
- UI workflow / component / accessibility;
- settings/persistence policy;
- documented operational limitation or compatible refactor.

These are **examples only**. Commit when a coherent improvement exists; do not make seven commits if only one actual change exists. Prefer descriptive conventional titles, such as feat(groups): enforce owner transfer policy (#NN), fix(realtime): bound typing fan-out (#20), feat(web): render unread direct messages (#NN).

Every commit must carry the correct actual author identity, have changed source, and be traceable to its issue. Workers do not rewrite accepted main commits or fabricate historical commits. The 1,300-PR objective does not override software integrity.

## 7. High-throughput batching

Three workers can work in parallel on separate issues, but main integration is serialized for each merged PR. Start a batch of three independently grounded issues (one per worker). The workers can stage next ready issues in parallel while Kirch opens and integrates finished PRs. After each merge, upcoming branches rebase or reconcile on current main without overwriting existing worker commits. If multiple PRs target the same contract, declare dependency order before writing.

Suggested lifecycle:
- Wave 0: reconcile open #9 and diverged P3 branch, protect current DM/session behavior.
- Wave 1: native startup + browser enrollment follow-through + group authority.
- Wave 2: DM and groups complete end-user workflows.
- Wave 3: LAN content storage and attachments.
- Wave 4: Telegram archive simulation and durable jobs.
- Wave 5: cross-platform native/web integration and settings.
- Wave 6: recovery, security boundary hardening and release-candidate code.
- Wave 7+: justified features, refactors, UX, accessibility, ops and robustness revealed by current source.

Do not schedule 1,300 identical tickets in advance. Expand the issue tree only when code inspection identifies a distinct useful behavior.

## 8. Existing engineering invariants remain binding

- Durable owner identity is not inferred from process, session or device.
- Server controls identity, permissions, role changes and membership.
- Nonmembers cannot read or send another DM/group/file; clients are not authority.
- Persist before broadcast; idempotency and deterministic cursor history.
- Revoked credentials/sessions must not continue receiving realtime data.
- Discovery is untrusted; native pairing confirms certificate fingerprint.
- Server TLS key and Telegram bot token never ship to clients.
- Telegram is optional archival tier, never realtime transport; no false E2E claim.
- SQLite migrations preserve accepted records; no destructive schema shortcuts.
- No dependency, source history or secrets accidentally overwritten to speed PR counts.

The requested sprint skips CI and tests, not the requirement to be truthful about security or unfinished implementation. Do not label code as production-ready without future verification.

## 9. Required worker return format — no vague summaries

Each worker returns in its own issue:

    WORKER: <Jane|Lucien|Nox>
    ISSUE: #NN
    SOURCE_MAIN_SHA: <40-char SHA>
    BRANCH: <exact branch>
    HEAD_SHA: <40-char SHA>
    PARENT_SHAS: <if relevant>
    CODE_PATHS_CHANGED: <paths>
    COMMITS: <SHA + one-line actual behavior per SHA>
    CONTRACT_DIFF: <API/DB/UI effects>
    SECURITY/PERSISTENCE_IMPACT: <what changed>
    CROSS_LANE_DEPENDENCIES: <issue URLs>
    CURRENT_CAPABILITY: <precisely what user can do>
    NOT_IMPLEMENTED: <remaining gaps>
    BUILD: NOT RUN / actual result
    TESTS: NOT RUN / actual result
    CI: NOT REQUESTED / actual result
    PR_READY: YES/NO
    KNOWN_RISKS: <specific>
    NEXT_ISSUES: <proposed atomic work>

Kirch's integration return adds PR number, actual PR author, reviewer request/result, merge method and exact SHA, issue state and updated GitHub merged-author count.

## 10. Next dispatch

Read exactly:
- 00-HANDOFF/INTERLAN_JANE_BACKEND_MASS_DELIVERY.md
- 00-HANDOFF/INTERLAN_LUCIEN_WEB_MASS_DELIVERY.md
- 00-HANDOFF/INTERLAN_NOX_DESKTOP_MASS_DELIVERY.md

The GitHub assignments are work routing, **not** proof that independent autonomous worker sessions are running. Sessions or workers must be launched and have their actions verified separately.

**END OF KIRCH COMMAND DIRECTIVE.**
