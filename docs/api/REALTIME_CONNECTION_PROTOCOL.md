# Authenticated INTER-LAN SignalR connection protocol

**Sources:** `src/InterLan.Server/MessagingEndpointMappings.cs`, `Realtime/ChatHub.cs`, `Realtime/RealtimeTicketStore.cs`, `Realtime/RealtimeConnectionRegistry.cs`, `Realtime/TypingDispatchGate.cs`, and browser `web-client/src/realtime/connection.js`.

## Authentication path and ticket lifetime
The server issues short-lived realtime tickets through authenticated `POST /api/v1/realtime/ticket`. Current mapping requests a **45-second** ticket lifetime; `RealtimeTicketStore` caps expiry at the authenticated session expiry and rejects expired sessions. The ticket is random, stored by hash, and consumed **once**. A failed/used/expired ticket must never be silently reused.

The SignalR hub accepts an authenticated bearer header for native/HTTP-supported clients, or a one-time `?ticket=` query value for browser WebSocket transport. The resolved session is checked again against durable enrollment session authority. The ticket is an ephemeral transport credential, not a replacement for user/device authorization.

## Browser transport flow
1. Have a valid bearer session from an approved join exchange.
2. Call the ticket endpoint with the bearer token over the current same-origin HTTPS connection.
3. Open the authenticated WebSocket at `/hubs/chat?ticket=<fresh-ticket>` using WSS on HTTPS. Avoid putting bearer or long-term device credential into URLs.
4. Complete standard SignalR JSON handshake (`{"protocol":"json","version":1}` followed by U+001E separator).
5. Process bounded JSON messages and authorized server events. Do not treat connection establishment as confirmation of durable message delivery.
6. On socket failure, refresh the ticket first, back off reconnection, and fetch missed durable conversation history by cursor.
7. On logout/revocation, tear down the socket, timers and old subscriptions. Old tickets should not renew access.

## Hub groups, presence and membership
After principal validation, the hub registers the connection in the server's revocation registry and adds it to `user:<user-id>` and `authenticated` connection groups. Presence state is ephemeral and should be interpreted as a current server-observed connection count. Session and device revocation are registered as transport abort actions, with durable authorization still determining future access.

A realtime event must not be treated as an entitlement to data. Direct message history and send APIs separately enforce conversation membership. Clients should refetch authoritative history after reconnect or missed events.

## Typing state
`SetTyping(Guid conversationId, bool isTyping)` requires a validated authenticated principal and direct conversation membership, checked before touching the suppression cache. After PR #28, repeated `typing=true` notifications are bounded by a **2-second** per-user/per-conversation dispatch interval; the default cache idle lifetime is **12 seconds** and maximum size **4096 entries**. `typing=false` removes cached state immediately and is broadcast to currently authorized other members. On last user disconnect, hub attempts a member-scoped stop notification. This cache is process-local: it is neither durable messaging nor a multi-server consensus mechanism.

## Observable event handling
The browser realtime adapter recognizes `MessageCreated`, `MessageEdited`, `MessageDeleted`, `ReceiptUpdated`, `TypingChanged` and `PresenceChanged`. Clients should ignore unknown event targets safely, deduplicate message IDs, and compare events with server persistence. Typing/presence indicators expire locally. A stale socket must not send events for another active account after a user switches sessions.

## Failure and privacy modes
- HTTP 401/403 for ticket issuance: treat as authorization failure, not ordinary network retry.
- WebSocket close/handshake failure: obtain a fresh ticket and use bounded retry.
- Expired/revoked device: stop transport and return to explicit sign-in/pairing recovery.
- Failed broadcast: do not mark messages delivered; persisted history remains source of truth.
- Never log bearer, realtime ticket, invite token or device renewal secret. Framework request-start logging was restricted in `InterLanServerHost` partly to avoid query-secret leakage.

**Validation:** source-reference only; this document does not assert successful transport interoperability, browser compatibility, load handling or security test results.
