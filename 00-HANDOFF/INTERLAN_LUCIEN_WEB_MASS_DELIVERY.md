# KIRION SMITHY — LUCIEN BROWSER / ANDROID WEB PRODUCT CODE DRIVE

**Dispatch:** LANE B — @Kirion-Lucien
**Human technical authority / PR owner:** Kirch Ivan Balite / @Kirch-Nairu
**Parent campaign:** #16
**Primary parent issue:** #18
**Merged predecessor:** Issue #21 / PR #23 (browser invite enrollment and memory-only approval exchange)
**Publication:** #25
**Snapshot:** main@679ee48de3f6989638b013dadc4b57d7a7b68101 — re-resolve before every branch
**Priority:** write real working React/browser code; no new test suites or CI architecture
**Session status:** handoff issued; independent worker execution has not been confirmed

## A. Your complete freedom and mission

Turn INTER-LAN from a LAN server-status web preview into a credible **usable Android-friendly LAN messaging client**. Design polished interaction patterns, real state management, composable views, sensible mobile/desktop responsive behavior and recovery. You may overhaul web-client code structure, split files, implement components/hooks/state machines, improve navigation and choose an efficient clean architecture. You do not need to ask Kirch for permission for each visual or state feature inside your path ownership.

The owner machine stays server authority. Browser is a **client**, never owner server. No false claim of native certificate pinning, E2E, permanent pairing or offline delivery unless actually implemented. Keep user authentication and membership authoritative server-side.

Kirch authors the GitHub PR using the Kirch-Nairu linked account after inspecting your source return. Your own code commits retain truthful actual author identity; co-author trailers for Kirch belong only where Kirch materially worked on that exact commit. Do not impersonate or relabel accounts.

## B. Mandatory read sequence and current facts

1. AGENTS.md.
2. 00-HANDOFF/INTERLAN_KIRCH_GOLD_MASS_CODE_CAMPAIGN.md and INTERLAN_INVARIANTS.md.
3. 00-HANDOFF/INTERLAN_V1_CODE_CAMPAIGN.md, INTERLAN_KNOWN_RISKS.md and current source tree.
4. web-client/src/main.jsx, web-client/src/serverInfo.js, web-client/src/styles.css, web-client/package.json, web-client/vite.config.js.
5. src/InterLan.Contracts/EnrollmentContracts.cs and ChatContracts.cs.
6. src/InterLan.Server/EnrollmentEndpointMappings.cs and MessagingEndpointMappings.cs.
7. src/InterLan.Server/Realtime/ChatHub.cs and RealtimeTicketStore.cs (for actual realtime auth path).
8. src/InterLan.Application/InterLanApiClient.cs and InterLanRealtimeClient.cs to understand canonical native API semantics.
9. Live PR #23 and merge SHA 52e540fdedcba527b6fd1f93105e7004984bff2c; avoid reimplementing that diff as though missing.
10. Current issues #18 and #16 and Jane's published contract diffs.

The current browser client already:
- can fetch server information with retry, timeout and offline state;
- can submit a join request against /api/v1/join using a random memory-only enrollment secret;
- can exchange approved request against /api/v1/join/exchange;
- keeps returned session metadata/bearer token only in component memory;
- can request logout;
- has responsive join form styling.

It **does not yet** provide a real chat thread, group list, durable browser pairing across page reload, usable file UI, or a verified end-to-end mobile flow. The join flow has no auto-poll; manual Check Approval is exposed. It intentionally discards the device renewal credential from persistent browser storage.

Do not claim these incomplete features exist. The server-side owner approval, role authority and sessions are not substitutes for actual browser UX.

## C. Strict lane ownership

Primary:
- web-client/src/main.jsx (refactor into views rather than keeping a giant file)
- web-client/src/serverInfo.js
- web-client/src/styles.css
- new web-client/src/api/*, auth/*, state/*, components/*, pages/*, hooks/* as needed
- web-client/package.json for real dependencies only, respecting reproducibility
- web-client index/config and assets as justified by product work.

Excluded from autonomous modifications: src/InterLan.Server, src/InterLan.Infrastructure, src/InterLan.Contracts, src/InterLan.Desktop and native credential policy. For missing API fields, create a dependency issue tagging Jane, with exact request/response proposal and consuming UI behavior. For shared session design decisions, coordinate with Nox's native pairing direction; browser capability is different.

Use the existing React/Vite project; don't replace the entire stack merely to create activity. Add dependencies only for clear functionality, explain their role and compatibility. Avoid CDN scripts, inline unsafe HTML and origin-mismatched bundles (PRs #11/#14 established integrity concerns).

## D. First code wave — authentication and browser session UX

Suggested natural issue candidates:
1. Extract enrollment API into a typed JS adapter with safe response mapping, no secrets in console logs.
2. Replace monolithic enrollment state with explicit idle/submitting/pending/approved/rejected/error transitions.
3. Add controlled resubmission and cancellation UX without inventing server capabilities.
4. Add timeout/AbortController support and disabled duplicate-submission behavior for join/exchange.
5. Handle 401, 400, 409, 429 and temporary 5xx/network failures distinctly; show safe guidance.
6. Improve owner approval polling only within bounded network behavior, with browser/tab visibility backoff and cancellation. If no approval-status endpoint exists, keep explicit user polling rather than flooding exchange attempts.
7. Build an in-memory authenticated fetch transport that applies bearer token only to same-origin API calls, never arbitrary URLs.
8. Add session expiration countdown and deterministic transition back to unauthenticated screen.
9. Implement authenticated current-user identity/state bootstrap using actual existing contract fields, not invented user profile APIs.
10. Add session logout, server-side revocation attempt and local sensitive state clearing.
11. Resolve browser reload persistence contract. Existing exchange returns a device credential but the client intentionally does not save it; do not silently call this persistent pairing. If persistence is desired, specify a defensible browser credential/session design with Jane before storing anything sensitive.
12. Show explicit certificate/trust onboarding guidance: self-signed LAN server HTTPS can require user action; browser cannot emulate pinned native TLS validation.
13. Add account/device status and clear revoked/expired session messages.
14. Add zero-secret diagnostics useful to owner support without leaking invites, bearer tokens, device credentials, or request IDs that could facilitate replay.

These are candidate real features, not 14 mandatory trivial PRs. Group atomic and interdependent UI/logic as appropriate.

## E. Second code wave — a real direct-message workspace

The server has authenticated direct conversation routes, directory search, ordered history and sends; inspect actual ChatContracts and route mappings before consuming.

Required user journey:
1. Approved client enters a responsive authenticated app shell.
2. Sees current signed-in identity, server name and connection state.
3. Searches actual users (no demo/fake contacts in production).
4. Opens or finds canonical direct conversation by actual otherUserId.
5. Loads conversation summaries, unread counts and latest messages using server projections.
6. Opens thread and retrieves ordered bounded history or recent-page cursor.
7. Renders own vs recipient message bubbles with semantic timestamps and stable IDs.
8. Composes a message, creates a unique clientMessageId and sends through authenticated endpoint.
9. Shows sending/sent/failed UI based on actual server response, not local optimism as proof of server durability.
10. Handles retry without duplicate persistence using the original idempotency key for same logical message.
11. Implements cursor catch-up after reconnect without duplicating client state.
12. Implements scrolling/pagination that avoids jumping to the bottom when reading older messages.
13. Displays sender/recipient read and delivered state only when actual server receipt contract supports it.
14. Supports edit/delete/reply policy only as routed on current backend; no fake option that silently does nothing.
15. Supports safe empty, offline, expired, revoked, loading and recovery states.
16. Preserves message text and formatting as plain text, not dangerous HTML injection.
17. Adds accessible keyboard focus, composer submit/cancel, focus restoration and screen reader status updates.

Do not use fake static messages or placeholder counts as final UX. Every real network action must be backed by an existing endpoint or explicit cross-lane dependency.

## F. Third code wave — authenticated realtime without leaking credentials

The backend uses SignalR ChatHub with bearer authorization or a one-time realtime ticket. Before integration, inspect actual route/event contract and supported browser transport.

- Negotiate a short-lived ticket using authenticated /api/v1/realtime/ticket if the server/browser transport requires it.
- Don't place a long-lived bearer or device credential in an easily logged URL query.
- Ensure websocket reconnect never falsely reuses a consumed one-time ticket.
- Refetch missed messages by cursor on reconnect, not by assuming event delivery was complete.
- Send typing indications only for the current authorized conversation; cooperate with Jane's #20 fan-out throttling.
- Show typing/presence indicators as ephemeral data that expire, not permanently "online."
- Dispose subscriptions on logout, unmount, server switch and token revocation.
- Avoid leaking events from previously viewed conversations into the active thread.
- Handle duplicate events and out-of-order event arrival defensively.
- Handle browser background/online/offline visibility changes without retry storms.
- Do not mark delivered/read until meaningful user receipt state transition occurs.
- Use resilient connection state UX: connecting, connected, reconnecting, unauthorized, offline.

Do not call SignalR integration complete merely because HTTP messaging works.

## G. Fourth code wave — groups and membership UX

Wait for Jane's exact current API contract or consume existing safe group routes if integrated:
- group inbox/list and current user's role;
- create group, rename/topic/avatar with correct role-gated affordances;
- invite/add/remove member workflows grounded in server response;
- owner/admin/member moderation/permissions UI;
- group membership detail, audit/system event timeline;
- group message composer/history/cursor and realtime fan-out;
- leaving/removal revokes client surface promptly; clear unauthorized cached data;
- unread summaries, group search, message media views;
- separate role-accessible controls, without treating hidden buttons as security.

Every privileged action must still be denied correctly by backend, regardless of UI hiding.

## H. Fifth wave — files, polish and ambitious product features

After actual P4 API ships:
- bounded upload selection/preview, file type/size policy and progress;
- cancel/retry with server confirmed result, not guessed "done";
- authorized attachment download and safe filename handling;
- archive status display as PENDING/ARCHIVED/FAILED without inventing Telegram behavior;
- image/document preview with sanitized content/URL handling;
- quota and owner policy UX when available;
- mobile bottom navigation, sidebar/drawer, message search, bookmarks/pins, notifications, theme controls, responsive grids, touch targets, keyboard and screen reader support;
- intentional 320px layout, landscape tablet view, clipboard-safe actions and consistent loading/error shells;
- local-first UX that explicitly differentiates server-durable data and temporary browser state.

Invent additional features when they deliver real end-user value. Keep server authorization and privacy intact.

## I. Continuous atomic code and PR loop

Each feature is a genuine code issue linked to #16 and #18:
- inspect live main and source SHA;
- document START issue and owned paths;
- new branch from latest main;
- implement actual code in one or several meaningful commits;
- annotate contract dependencies and unfinished states;
- return actual HEAD SHA to Kirch;
- Kirch opens a genuine PR through Kirch-Nairu; reviewer is Jane or Nox;
- do not mark a requested review approved;
- merge only after GitHub reports success and synchronize next branch with current main.

Possible good source commit names:
    feat(web): extract authenticated LAN API adapter (#NN)
    feat(web): implement approved-session inbox (#NN)
    feat(web): compose and submit idempotent DM (#NN)
    feat(web): catch up history after reconnect (#NN)
    feat(web): render accessible group membership controls (#NN)

Do not force meaningless split commits or false Kirch coauthor trailers. Kirch can author his own real integration source changes; worker's genuine source commits remain worker-authored.

## J. Handoff record to deliver

Return on each child issue with:
- source main SHA and branch;
- exact changed paths and commit SHAs;
- screenshot/description of actual UX if available (do not fabricate screenshots);
- API routes called, payload assumptions, error behavior and live backend dependency;
- whether a credential is retained, where, for how long, and how cleared;
- what works only in memory vs after refresh/reconnect;
- unimplemented screens/flows (explicit);
- reviewer for the eventual Kirch-authored PR;
- build: NOT RUN when not run; tests: NOT RUN; CI: NOT REQUESTED;
- next 3–10 grounded feature issues with actual changed behavior;
- PR_READY yes/no and final HEAD SHA.

**This assignment requires functioning product code, truthful commit authorship, and verifiable handoff to the Kirch account — not invented PR counts.**

**END — LUCIEN HANDOFF**
