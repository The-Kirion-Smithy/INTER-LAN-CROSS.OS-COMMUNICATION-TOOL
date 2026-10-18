# KIRION SMITHY — NOX AVALONIA / NATIVE CLIENT / OWNER RUNTIME CODE DRIVE

**Dispatch:** LANE C — @Kirion-Nox
**Human technical authority / PR owner:** Kirch Ivan Balite / @Kirch-Nairu
**Parent campaign:** #16
**Primary parent issue:** #19
**Merged predecessor:** Issue #22 / PR #24 (real owner server start/stop)
**Publication:** #25
**Snapshot source:** main@679ee48de3f6989638b013dadc4b57d7a7b68101 (refresh before each branch)
**Work mode:** aggressive meaningful code writing; no new tests or CI pipelines this campaign
**Execution:** This is a handoff, not confirmation that a separate Nox agent session is already running

## A. Your mission and freedom

Turn a native Avalonia window with basic owner-server controls into a coherent, approachable cross-platform **owner/server and paired-client desktop application**. You own the desktop user experience, composition, navigation, pairing UX, native messaging workspace, owner settings and local lifecycle coordination. You may design the entire visual system, create new view models/components, restructure UI modules and add coherent native utility features without asking permission for each choice.

The canonical server remains durable backend authority. Starting a process must not grant an owner role or bypass the one-time bootstrap process; a client does not become owner merely by joining. Native clients must confirm/pin a server certificate fingerprint and preserve secure pairing state appropriately for their platform. Never use global "trust any certificate" shortcuts.

The Pull Shark-authoring workflow is **Kirch-owned PRs**: deliver a real source branch to Kirch; Kirch-Nairu opens the PR through his own authenticated account. Your own source commits retain their actual Git author. Kirch's coauthor trailer is appropriate only for specific commits to which he genuinely contributed. Actual Kirch integration fixes are authored by Kirch.

## B. What already exists vs what does not

Existing source:
- src/InterLan.Desktop/MainWindow.cs
- src/InterLan.Desktop/OwnerServerRuntime.cs
- src/InterLan.Desktop/App.cs and Program.cs
- src/InterLan.Server/InterLanServerHost.cs, InterLanServerInstance.cs
- src/InterLan.Application/PairedClientSessionManager.cs
- src/InterLan.Application/ClientPairingStateStore.cs
- src/InterLan.Application/PinnedTransportFactory.cs
- src/InterLan.Application/InterLanApiClient.cs
- src/InterLan.Application/InterLanRealtimeClient.cs
- src/InterLan.Infrastructure/LanDiscoveryClient.cs
- src/InterLan.Contracts/EnrollmentContracts.cs and ChatContracts.cs
- src/InterLan.Server/EnrollmentEndpointMappings.cs

Issue #22 / PR #24 on current main implemented Start Owner Server / Stop Owner Server controls around existing OwnerServerRuntime, local durable data location and status reporting. This is NOT a complete owner setup: the UI itself still says to complete one-time owner bootstrap through an API. Join Server UI is intentionally disabled. No finished native DM/group conversation workspace is present.

Read these sources instead of blindly rewriting them. Avoid deleting the now-working server lifecycle or creating a second independent Kestrel process.

## C. Scope and explicit file ownership

Primary:
- src/InterLan.Desktop/** (complete authority over native UX and composition)
- native-oriented new application adapters only with prior cross-lane interface notice
- proposals for secure native storage/cross-platform packaging in child issues.

Shared only with coordination: src/InterLan.Application/**, because both Jane's contract work and your typed pairing implementation depend on it. Do not autonomously edit Jane's server/storage/migrations or Lucien's web client. If the existing backend doesn't support your workflow, open a dependency issue with an exact route/contract request.

Use existing .NET 10 + Avalonia architecture. Do not replace native with Electron, build an unrelated server, or hardcode one OS's paths into every platform.

## D. First wave — real owner bootstrap wizard

User story: on first launch, a human can **Create Server**, enter local owner setup, start the owner host, bootstrap one durable owner securely, inspect status and stop/restart without losing server identity or messages.

Natural implementation slices:
1. Separate window shell/navigation from owner runtime event handlers and status view model.
2. Show mode chooser Create Owner Server vs Join Existing Server (only enable workflows that exist).
3. Choose default owner data directory using platform APIs; allow deliberate custom directory selection with user consent.
4. Show server display name, owner username, owner display name, strong password input and confirmation.
5. Use PasswordBox or equivalent secure native Avalonia input, never an ordinary persisted textbox.
6. Bind the actual in-process server lifecycle to the owner wizard; don't launch duplicate hosts.
7. Retrieve current server state via the server-info/bootstrap API and distinguish configured vs unconfigured.
8. Perform only local loopback owner bootstrap through the existing /api/v1/bootstrap/server contract. It is localhost-gated; no remote/browser bootstrap path.
9. Explicitly surface TLS fingerprint and first-use trust guidance; bootstrap to local self-signed host only through a controlled transport, never globally disable certificate validation.
10. Handle one-time bootstrap rejection (already configured), wrong inputs, port collision, disk failure, startup cancellation and partial initialization.
11. Persist durable server identity and settings through existing server service; do not infer owner based on process.
12. Add owner started/stopped/configured banners based on actual runtime, not a clicked-button flag.
13. Shutdown with cancellation and resource cleanup; preserve database and certificate identity across restart.
14. Prevent double-start/double-stop races with a serialized UI state machine.
15. Add owner login UI and session renewal using actual backend contract, with clear token lifecycle.
16. Provide owner-facing invite issuance and pending join approval/rejection UI against server routes.
17. Show approved device list, revocation, session listing and server settings based on live backend state.
18. Show clear "restart required" notification for listener-affecting settings changed through canonical store.

These are real feature opportunities, not a required number of PRs. Split issues along actual contract/UI behavior boundaries.

## E. Second wave — native LAN client connection and trust path

A complete native join must not fake certificate security:
1. Show LAN discovery candidates as **untrusted advertisements**; manual HTTPS hostname/IP path must always exist.
2. Display candidate server name/URL/fingerprint, but do not silently trust advertised fingerprint.
3. Require human confirmation of trusted identity before pairing.
4. Reuse PinnedTransportFactory and protected ClientPairingStateStore rather than making new insecure HTTP transport.
5. Support invite input, device name/platform and user identity, with enrollment request submission.
6. Handle owner approval wait, rejection, retry, expiry and explicit exchange using the existing server contract.
7. Persist pairing state only through protected adapter for the current OS. Windows DPAPI-backed protection exists; macOS/Linux still have documented weaker fallback concerns. Do not claim equivalent protection where no native secret store is wired.
8. Reopen a paired client after restart using PairedClientSessionManager.LoadAndRenewAsync.
9. Ensure certificate fingerprint mismatch fails closed; no automatic trust after certificate rotation.
10. Handle revoked device, expired bearer, rotated credential, lost local state, wrong server host and offline network distinctly.
11. Provide intentional unpair/logout UI that triggers durable server revocation where possible and clears local pairing state.
12. Support multiple known server profiles, but never combine cached tokens/fingerprints across servers.
13. Make reconnection lifecycle explicit and bounded, avoiding multiple active SignalR connections after navigation.
14. Show friendly progress and recovery diagnostics without printing raw credentials/certificate private keys.
15. Keep native transport and authorization semantics aligned with the browser's actual backend API, not a privileged desktop-only bypass.

Where actual C# application APIs lack a method, document the required typed adapter and create a cross-lane issue for Jane if server changes are needed.

## F. Third wave — native direct messaging product

Use existing typed InterLanApiClient / InterLanRealtimeClient interfaces, extending them only with coordinated contract:
- authenticated user directory and search;
- canonical one-to-one thread creation;
- inbox with last message, pin/archive and unread state where current API supports;
- page/history navigation with deterministic cursor ordering;
- message compose, send with clientMessageId, idempotent retry and server-confirmed state;
- live message receipt via SignalR and reconnect catch-up;
- typing indicators with finite display lifetime;
- delivered/read receipts grounded in recipient action;
- edit/delete/reply according to backend policy;
- accessible text input, keyboard shortcuts, focus handling, virtualized list for many messages;
- offline/resuming/expired/revoked session UX;
- local drafts without turning transient drafts into falsely durable messages;
- thread switch and event-subscription disposal;
- scroll history preservation, selected thread controls and useful empty states.

No fabricated chat traffic in product code. Do not label any local optimistic message as server persisted until confirmed.

## G. Fourth wave — groups / files / owner management

Once Jane's group authorization and P4 attachment endpoints are integrated into main:
- group inbox and membership-aware navigation;
- real create/edit group and role-based controls, aligned with server roles;
- add/remove/promote/demote UI with clear owner-only controls;
- group message history/realtime/membership invalidation;
- file choose/upload with progress, quota and type-policy indicators;
- authorized download/open and reliable local destination handling;
- cross-platform path, Unicode, case-sensitivity and filesystem permission behavior;
- archive status display as actual server state, not invented Telegram success;
- owner device approvals, policy settings, server storage and data snapshots;
- access and membership changes reflected promptly after realtime revocation;
- user preference screens, notifications, unread counters, search and security diagnostics.

Choose meaningful application UX, not just placeholder tabs. Never present capabilities that aren't wired into server endpoints.

## H. Fifth wave — cross-platform native composition

Architect for Windows, Linux and macOS from the same application, with platform-aware services:
- native configuration, directory selection, logging levels and permission treatment;
- platform credential store behavior, with explicit secure fallback limitations;
- certificate trust and fingerprint display;
- process lifecycle on window close/reopen;
- high-DPI scaling, minimum layouts, keyboard navigation, assistive technology labels;
- safe error panels that don't leak request secrets;
- graceful owner mode server failures, connection loss and data volume problems;
- predictable local publish tree including desktop binary + required server/runtime resources;
- packaging capability definitions as source contracts, not CI/release workflow development this sprint;
- manual product onboarding and operator-friendly setup docs based on actual screens.

Document what Windows/Linux/macOS behavior is only design-time vs executed. No pretending to have run on other operating systems.

## I. Architecture suggestions — optional but grounded

A useful native structure may contain:
- AppShell + ModeRouter
- OwnerSetupView / OwnerDashboardView
- JoinServerView / PendingApprovalView
- IdentityTrustView / PairingStateView
- DirectInboxView / DirectThreadView
- GroupWorkspaceView / AttachmentPanel
- NativeSessionCoordinator
- OwnerRuntimeCoordinator
- platform-specific SecurePairingStateAdapter

These are suggestions, not a mandate to create empty files. Implement each only when it has actual logic or visible behavior. Resist spaghetti event handlers in one MainWindow file when a coherent view model and UI state separation can reduce coupling.

## J. Git and authorship doctrine

Worker code workflow:
- Read current main and changed dependencies, not only the historical issue snapshot.
- Record issue, SHA, branch and owned files at START.
- Code a genuine feature or fix with logical, reviewable commit boundaries.
- Preserve actual worker source author; never spoof Kirch.
- Send an exact HEAD SHA and source-path list to Kirch.
- Kirch can add authentic integration source commits and opens the qualifying PR under Kirch-Nairu.
- Another account performs any actual review of Kirch-authored PR; Kirch cannot approve his own authored PR.
- Retain normal merge history; capture merged=true and exact merge SHA before counting.

Example substantive commits:
    feat(desktop): implement first-run owner setup wizard (#NN)
    feat(desktop): wire local owner bootstrap API (#NN)
    feat(desktop): require human confirmation of certificate fingerprint (#NN)
    feat(desktop): restore approved native device session (#NN)
    feat(desktop): render durable direct-message inbox (#NN)

Do not pad history through fake/empty commits or append false co-author trailers. Real Kirch participation may use the verified Co-authored-by trailer documented in the campaign command directive.

## K. Return format and explicit pending work

On your issue, return:
- baseline main SHA; branch; current HEAD;
- actual updated files and completed UI behaviors;
- actual source commit SHAs, with line-level responsibility where important;
- typed/server API dependencies and changes requested of Jane;
- security handling for fingerprint, bearer, device credential and local pairing state;
- what works in owner mode and what works in client mode;
- exactly what is still disabled/placeholder;
- whether data and identity should survive restart and whether this was actually run;
- build: NOT RUN if not run; tests: NOT RUN; CI: NOT REQUESTED;
- PR_READY / blockers and next 3–10 specific issues with reason.

**Native polish and genuinely integrated runtime beats empty screens and inflated counters. Deliver actual source for Kirch to submit.**

**END — NOX HANDOFF**
