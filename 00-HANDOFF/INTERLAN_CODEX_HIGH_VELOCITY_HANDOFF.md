# INTER-LAN — CODEX HIGH-VELOCITY EXECUTOR HANDOFF

**Issued:** 2026-10-19 (Asia/Manila). **Control issue:** https://github.com/The-Kirion-Smithy/INTER-LAN-CROSS.OS-COMMUNICATION-TOOL/issues/16. **Handoff issue:** #100.

**Repository:** `The-Kirion-Smithy/INTER-LAN-CROSS.OS-COMMUNICATION-TOOL`. **Technical authority:** Kirch-Nairu. **Starting verified main:** `5f0f08ba9254ad3b55f930d9aea5ab9f9242135a` following merged PR #99. This SHA is an audit anchor, not a pinned future branch base. Refresh LIVE main before edits. **Starting global merged PR author count:** 176 for Kirch-Nairu; repository-local 42; goal 1,024 and 848 remaining. Refresh counts before each batch; search indexing can lag and a PR author count is not proof of achievement eligibility.

## EXECUTE NOW — DO NOT JUST PLAN

You are Codex, the autonomous source-code implementation and GitHub delivery executor. Inspect the actual repository and run a high-throughput issue → branch → code → commits → review → PR → normal merge → verified merged loop throughout your **active** execution session. Use native repo/filesystem/Git/GitHub workflows, not manual PowerShell queues, a local AI dependency or a predetermined manifest of 848 branches. If available, launch REAL independent subagents with isolated worktrees and nonoverlapping file ownership; issue assignment alone is not a launched worker. Do not falsely promise work continuing once the active Codex session ends.

**First read and refresh:** `AGENTS.md`, this file, `00-HANDOFF/INTERLAN_EXECUTION_STATE.md`, `00-HANDOFF/INTERLAN_INVARIANTS.md`, live main, current source, open issues, open PRs, and current GitHub login. The live repo overrides old phase snapshots, old SHA references and any conflict in this handoff.

**Autonomy / throttle:** Broad authority to edit backend, domain, contracts, migrations, browser, Avalonia desktop, architecture, refactors and worthwhile documentation. Design complete useful features, not 30 tiny cosmetic PRs. Work on up to three independent issues in parallel, integrate merges serially, and start the next wave immediately. Choose routine engineering decisions without permission prompts. Run relevant targeted builds and safety checks when possible, but avoid CI babysitting and repetitive test-only contributions. Explicitly record NOT RUN when not executed. Do not forge independent approvals.

## FIRST SOURCE BACKLOG — REVALIDATE, DO NOT REDO ACCEPTED WORK

1. **Issue #35 — group message persistence and authorized fan-out (backend):** real durable group text messages, sender/client idempotency, ordered cursor history, transactionally persist BEFORE SignalR, nonmember read/send denial, active membership validation before fan-out, and immediate removal/revocation cutoff. Preserve accepted #33 group authority, #28 typing gate and migration 008 data.
2. **Issue #40 — macOS/Linux secure native pairing:** add OS credential vault integration (Keychain, Secret Service/libsecret or reviewed secure alternative), fail closed where not available, preserve DPAPI behavior, safe migration and explicit recovery. No raw co-located AES key claimed secure.
3. **Issues #38/#34 — authorized attachments/files:** canonical server-side upload/download, current membership checks on every operation, server-computed SHA-256, upload name never treated as trusted path, size/quota bounds, atomic interrupted-write policy and typed browser/native UX.
4. Once #35 backend is accepted, wire real authenticated group message threads in browser/native with reconnect and session-revocation handling. Do not render decorative message UI claiming nonexistent API.
5. Independently discovered source gaps in security, role authorization, reconnect, accessible native/mobile UX, backup/recovery, optional Telegram archive (never realtime) and observability. Choose substantial justified changes based on live source evidence.

**Already integrated:** PRs #89/#91/#93/#95/#97/#99 (Kirch-authored), plus older #28/#33/#29/#32/#43/#45/#58 and client improvements. Do not repeat these. Recent source hardening covers missing pairing-key recovery, protected unique temporary files, protected Unix TLS PEM creation, unmanaged DPAPI zeroization, runtime config validation and PKCS#12 temporary buffer scrubbing. Last known merge `5f0f08ba9254ad3b55f930d9aea5ab9f9242135a`.

## REAL ENGINEERING DELIVERY LOOP

1. Refresh main SHA, open PRs and actual unresolved source. Identify a concrete unimplemented defect/feature that is not on another branch/PR.
2. Open a source-grounded issue (or select a specific existing one) with affected files, behavior, dependencies, security/persistence concerns and acceptance criteria; link #16.
3. Branch from fresh main per independent issue. Concurrent branches only with disjoint paths or explicit interface handoff; don't rewrite accepted branches or force-push main.
4. Implement complete working code. Commit coherent real source changes attributed to the actual human/agent author; no fake coauthors, empty commits, README spam or meaningless splits.
5. Inspect exact diff/HEAD and contracts; run focused builds and security-relevant checks when feasible. Never label unrun tests PASS. A safe failure or review blocker is a result; don't hide it.
6. Open PR only with proper authority. **For Kirch Pull Shark accounting, GitHub must identify Kirch-Nairu as the actual PR creator.** If Codex authenticates as another GitHub account, contribute truthful code/branches and return READY for Kirch to open the PR; do not impersonate/share secrets or claim a bot PR counts for Kirch.
7. Request real independent nonauthor review for high-risk changes; a request is not approval. Never self-approve. Merge normally only after enough evidence to understand side effects; never bypass failed auth, identity, revocation, migration or data-loss gates to reach a badge count.
8. Confirm GitHub reports merged=true, exact merge SHA and issue disposition. Refresh main and global GitHub merged-author count. Log results to #16 at least after each three-issue batch.
9. Immediately select the next useful independent issue. Continue successive waves in the active Codex session until exhausted, blocked by authority/security, user target reached or session ends. At exit provide precise resumption checkpoint.

## SAFETY INVARIANTS — DO NOT WEAKEN

- Durable owner roles come from canonical server-side auth, not host/process/client position. Exactly one canonical owner server; Android/web never host it.
- Native TLS fingerprint pin is independently verified, discovery is untrusted, never disable cert validation or silently rotate server identity.
- Never leak device credentials, passwords, private TLS keys, bot secrets or tokens into client bundles, URLs or logs.
- Server controls DM/group/file access and roles. Removed/revoked sessions/devices lose HTTP and realtime authority immediately.
- Persist before broadcast, enforce idempotency and deterministic cursor history; SQLite migrations are ordered/additive and preserve data.
- Clients cannot select server paths; file SHA-256 is computed on server; interrupt handling must not publish partial attachments.
- Server-readable messages are NOT end-to-end encrypted; Telegram is optional archive tier only.
- Git authorship and PR creator must be truthful. Normal merges preserve accepted history. No fake reviews or inflated PR farming.

## CURRENT SOURCE MAP / VERIFICATION

.NET 10 solution: `InterLan.sln`. Backend: `src/InterLan.Server` and `src/InterLan.Infrastructure`; migrations through accepted 008 group authority. Contracts: `src/InterLan.Contracts`. App services: `src/InterLan.Application`. Native Avalonia: `src/InterLan.Desktop`. Browser/Android: `web-client`. The existing draft PR #9 is against a non-main rebaseline; do not merge without source reconciliation.

Historical C075 green checks apply only to historical source, NOT rapid 2026-10-19 merged source. Recent PRs were static-reviewed and normal-merged but subsequent cross-platform runtime/build/security acceptance was NOT RUN. Test status must be exact evidence, not assumed.

## CODEX LAUNCH MESSAGE

Open The-Kirion-Smithy/INTER-LAN-CROSS.OS-COMMUNICATION-TOOL on LIVE main. Read AGENTS.md, 00-HANDOFF/INTERLAN_CODEX_HIGH_VELOCITY_HANDOFF.md, the invariants, and issue #16. Switch to maximal autonomous, source-backed delivery. Implement issue #35 first, and #40 in an independent lane if real parallel workers exist; then #38. Keep running genuine issue → branch → source commits → reviewed Kirch-created PR → normal merge cycles within your active session. No local AI setup, fake activity, invented GitHub credit, or permission/security shortcuts. After each batch report exact SHAs, merged PRs, checks actually run, updated count and next issues. IMPLEMENT CODE — NOT ANOTHER PLAN.
