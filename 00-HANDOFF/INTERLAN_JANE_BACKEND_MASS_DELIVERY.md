# KIRION SMITHY — JANE BACKEND / REALTIME / GROUPS / STORAGE CODE DRIVE

**Dispatch:** LANE A — @Kirion-Jane
**Human technical authority / PR owner:** Kirch Ivan Balite / @Kirch-Nairu
**Parent campaign:** #16
**Parent implementation:** #17 (P3 reconciliation), #20 (typing event fan-out)
**Publication:** #25
**Snapshot predecessor:** main@679ee48de3f6989638b013dadc4b57d7a7b68101 (resolve current head afresh)
**Mission status:** AUTHORISED HANDOFF; NOT evidence Jane's separate session has begun
**Priority:** CODE WRITING; no new test/CI authoring this wave

## A. Command

Develop the server side of INTER-LAN decisively: a correct, useful LAN communication backend with durable owner and membership authority, meaningful groups, realtime messaging, attachment hosting and Telegram archival foundations. You may design new server features and refactor old internals where that produces demonstrably better behavior. Do not stop at issue descriptions: implement actual source and coherent commits.

Every future PR intended to count toward Kirch's Pull Shark must be created by @Kirch-Nairu **using Kirch's authenticated GitHub account** after you return a candidate branch/HEAD. Your legitimate source commits are attributed to your account; Kirch may create genuine integration commits and receive co-author attribution only when actually participating in the particular commit. Never change git author configuration to impersonate him.

### Read before changes, in this order

1. AGENTS.md and 00-HANDOFF/INTERLAN_KIRCH_GOLD_MASS_CODE_CAMPAIGN.md.
2. 00-HANDOFF/INTERLAN_INVARIANTS.md and INTERLAN_KNOWN_RISKS.md.
3. 00-HANDOFF/INTERLAN_V1_CODE_CAMPAIGN.md, INTERLAN_EXECUTION_STATE.md, P2_C100_ARCHITECTURE_REVIEW.md.
4. Current actual main source (not historical README): src/InterLan.Server, src/InterLan.Infrastructure, src/InterLan.Contracts, src/InterLan.Domain, src/InterLan.Application.
5. Existing migrations under src/InterLan.Infrastructure/Migrations.
6. The actual diff between main and KIRCH-INTERLAN-P3-GROUP-CHAT; inspect live ahead/behind again.
7. Current open pull requests, especially draft #9 which targets a separate rebaseline branch.
8. Current issue #17/#20 conversation for source ownership and dependencies.

Important: current main already has durable P2 DM/chat, receipt and session primitives; do not invent a parallel auth store, second SQLite migration engine, or second SignalR revocation registry.

## B. Source and file authority

Primary ownable paths:
- src/InterLan.Server/MessagingEndpointMappings.cs
- src/InterLan.Server/Realtime/ChatHub.cs
- src/InterLan.Server/Realtime/RealtimeConnectionRegistry.cs
- src/InterLan.Server/AuthorizationHelpers.cs
- src/InterLan.Infrastructure/ChatStore.cs
- src/InterLan.Infrastructure/EnrollmentStore.cs
- src/InterLan.Infrastructure/SqliteDatabase.cs
- src/InterLan.Infrastructure/Migrations/ (allocate numbers against current live tree)
- src/InterLan.Contracts/ChatContracts.cs and relevant new contracts
- src/InterLan.Domain/ relevant new domain types
- future src/InterLan.Infrastructure/ file and archive adapters

Shared contracts need a published source interface note for Lucien and Nox **before** breaking changes are made. No autonomous edits to web-client/** or src/InterLan.Desktop/**. Use new server/domain modules to reduce giant files.

The prior P3 experimental branch was observed 98 commits ahead and 31 behind main; these values are snapshots, not an instruction to merge unchanged. Build a **preservation map** by file, source SHA, dependencies, semantic delta and hazards. Reuse implementation where safe and reconcile with main's newer revocation, realtime tickets, client contracts and data model. Never force push the old branch, erase its changes, or mark old work accepted without inspecting it.

## C. Immediate ordered mission 1 — finish issue #20 cleanly

**Goal:** An authenticated client repeatedly sending typing=true must not flood current DM recipients with duplicate realtime messages. This must not create an authorization bypass.

Implementation guidance:
1. Read SetTyping in ChatHub and current GetDirectMemberIdsAsync authorization; membership permission check MUST occur before any throttling response.
2. Model ephemeral typing state using a bounded tuple of (authenticated user ID, conversation ID), not raw client-controlled caller identity.
3. Design a short dispatch suppression window for duplicate typing=true from the same author/conversation. The value is a parameter, not a hard-coded business promise; define its behavior and expiry.
4. Immediate typing=false should clear the suppression state and be emitted to other current members; reconnect/disconnect must not leave stale indefinite state.
5. Avoid race-prone check-then-add state, memory growth from abandoned conversations, and accidental cross-DM state bleed.
6. The existing SignalR groups must still limit events to authorized recipients; do not broadcast to the all-authenticated group.
7. Send no message body, secret, or bearer token in typing metadata.
8. Create one issue-linked real implementation branch from current main. Commit actual domain/runtime code as coherent atomic changes.
9. Record exact changed functions and behavior. No build or tests are requested in this wave; explicitly record NOT RUN, not PASS.
10. Return SHA to Kirch for opening one PR linked to #20. Reviewer must be a nonauthor worker account; Kirch cannot approve his own PR.

Acceptance by source inspection: a current member can signal typing; nonmember cannot; duplicate true calls are bounded; false clears promptly; state has a finite lifetime; unauthorized traffic does not enter the dispatch cache.

## D. Immediate ordered mission 2 — P3 branch reconciliation / groups

Treat P3 as a substantive subsystem, not a shallow port. Proposed natural independent issues and commits:
1. Inventory and map the diverged P3 branch source, identifying kept, revised and rejected deltas. Link exact source commit SHAs and file paths.
2. Validate group domain records and migration sequence against the current main schema. Never insert a second migration number or drop populated tables.
3. Implement create-group with server-minted group ID and creator OWNER role.
4. Implement owner/admin/member authority and one canonical permission evaluator.
5. Add update-name/topic/avatar metadata route under role policy.
6. Add membership invitation / acceptance / direct add as chosen, with explicit authorization rules.
7. Add member list scoped to current group members.
8. Add role promotion/demotion and prohibit implicit owner transfer.
9. Add controlled ownership transfer with safe last-owner behavior (if supported).
10. Add removal/leave rules, including owner departure constraints.
11. Add membership audit events and durable system messages as appropriate.
12. Add durable group message stream with deterministic order.
13. Add client-message ID idempotency within the proper author/group scope.
14. Add group history and cursor pagination with bounded limits.
15. Add group realtime send only after durable transaction commit.
16. Add realtime fan-out only to current members and immediate removal cutoff, including active connection scenarios.
17. Add group receipts or explicit unsupported semantics; no fake "delivered" state.
18. Add concurrent add/remove/send policy with unambiguous serialization.
19. Add group list, last-message, unread count and search projections for both clients.
20. Add transparent error mapping: unauthenticated, unauthorized, nonmember, invalid ID, deleted group, conflict.

These are candidate **actual engineering slices**, not pre-created spam tickets. Coalesce inseparable edits; separate meaningful independently reviewable behavior into distinct issues. Group membership is server-only authority; neither a browser UI nor a desktop control can override it.

## E. P4 LAN file hosting — implement in actual stages

Model server-authenticated file upload and download, with current DM or group membership authorization every time. Suggested features:
- upload policy, allowed MIME types, true server-side size and quota limits;
- sanitized display names and content-addressed physical storage;
- streamed hash and byte count, no arbitrary path writes;
- staging area, atomic promotion, interrupted upload recovery;
- durable attachment records linked to messages and uploader identity;
- per-recipient/group authorization when downloading; protected share links if designed;
- range download only when membership and content integrity remain valid;
- deduplicate identical content without giving unauthorized users evidence of other files;
- safe deletion/retention and eventual content garbage collection;
- cross-platform filesystem path normalization and case/Unicode concerns;
- durability on restart and deterministic recovery from missing files;
- quota projections and owner policy controls.

Create distinct issues for real subsystems. Do not expose owner filesystem paths, rely on user-provided MIME alone, trust names as paths, or mark incomplete staging bytes as a valid attachment.

## F. P5 Telegram archive and restore

Preserve the architectural boundary: Telegram is only an archive tier and cannot replace local LAN messaging or become realtime server authority.

Build:
- a server-only configuration model for archive destination and secret source;
- a client-safe status contract that never reveals bot token or chat secret;
- persistent archive jobs with LOCAL/PENDING/ARCHIVING/ARCHIVED/FAILED/RETRY semantics;
- idempotent dispatch, exponential retry/backoff and bounded queue policy;
- metadata linking attachment and Telegram file/message identifiers;
- confirmation semantics: ARCHIVED means Telegram actually accepted a durable archive action;
- restore by known artifact and SHA-256 verify before exposing;
- storage lifecycle/retention only after confirmed archive;
- outage handling: local product continues, jobs remain retryable;
- safe diagnostics, error policy and interrupted-job recovery.

No real Telegram secret is required or should be committed. Do not claim archival succeeded without actual API confirmation.

## G. Creative backlog beyond base V1

You're free to propose and implement coherent improvements as the code reveals the need: offline delivery receipts, presence freshness, unread projection caching, conversation pin/archive preferences, role audit introspection, edit/delete semantics, message search, pagination stability, event sequencing, cursor compaction, bounded retention, structured application errors, server maintenance toggles, concurrency/load isolation, device management, owner policy controls, notification event contracts, member muting/moderation, attachment relationship semantics, local export/import, reliable restart recovery.

Choose features that genuinely add behavior and can be traced to code. Do NOT turn a single string rename into multiple PRs.

## H. Cross-lane contract publication

For each API added/changed, post to #17 and tag the affected owner:
- issue + branch + SHA;
- exact method + path + required auth role;
- request/response JSON field names, nullability, semantics;
- possible response status codes;
- realtime event name + payload + persistence ordering;
- pagination/cursor behavior;
- migration number and durable compatibility;
- identity and revocation impacts;
- earliest main SHA which includes the contract;
- unresolved client integration blocker.

Lucien consumes web contracts, Nox consumes native typed-client contracts; do not silently change a field that either client already uses.

## I. Strict Git operating procedure

For each issue:
- Read current main SHA; branch anew.
- Never rewrite an accepted branch or force-push.
- Actual worker-authored commits remain actual worker-authored commits.
- Commit only coherent source improvements, with conventional commit title and linked issue.
- Before next slice, refresh source after accepted merges; handle conflicts explicitly.
- Return to Kirch with the READY record and exact SHA.
- Kirch opens the Pull Shark-qualified PR from his account; reviewer is another worker.
- Only GitHub confirmed merge produces a merged PR count.

Examples of real commits:
    feat(groups): persist canonical group membership (#NN)
    feat(groups): restrict role mutation to owner/admin (#NN)
    feat(groups): commit group messages before realtime publish (#NN)
    fix(realtime): suppress duplicate typing dispatch (#20)
    feat(files): stream content to validated staging store (#NN)

Do not force co-author trailers onto work to which Kirch did not contribute. If Kirch actually jointly designs or edits the specific commit, the standard co-author trailer may be used with the verified address in the master handoff.

## J. Worker return — no handwaving

Post to the issue:
- START main SHA; branch; owned files; existing conflicts/dependencies.
- Exact implementation SHAs and one-line material effect of each.
- Changed endpoint/contracts/schema identifiers.
- Security and data-preservation notes.
- Code that demonstrably exists vs remaining gaps.
- Proposed 3–10 next atomic issue titles grounded in inspected source.
- Build, tests and CI: NOT RUN when not run.
- PR_READY yes/no; reason; exact branch HEAD.
- No claim of autonomous other-lane code, completed PR, accepted review or merged status unless GitHub confirms.

Your responsibility is **substantive backend work and a truthful source handoff**. Kirch is the GitHub PR opener and final integration authority.

**END — JANE HANDOFF**
