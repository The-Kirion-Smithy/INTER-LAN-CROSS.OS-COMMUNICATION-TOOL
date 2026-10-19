# Authenticated group message history — current API

**Owner authority:** `src/InterLan.Server/GroupEndpointMappings.cs` and
`src/InterLan.Infrastructure/GroupMessageStore.cs`.
**Browser integration:** `web-client/src/api/groups.js`,
`web-client/src/GroupWorkspace.jsx`, and
`web-client/src/realtime/connection.js`.

All routes below are on the pinned owner's **same-origin HTTPS** endpoint. Supply
the active session bearer through the `Authorization` header, never a URL query.
These endpoints require **current active group membership** as checked by the
owner's SQLite store. Previously cached messages are not an authorization grant.

## Open a group at the most recent messages

```http
GET /api/v1/groups/{groupId}/messages/recent?limit=50
Authorization: Bearer <current-session>
```

- `groupId`: an existing group UUID.
- `limit`: optional; defaults to 50, clamped by the HTTP route to 1–200.
- `beforeMessageId`: optional UUID from this **same group**, used to retrieve
  messages older than that message. A cross-group or unknown cursor is a 404.
- Results come from a read transaction that checks membership and applies
  the cursor against one SQLite read snapshot.
- The database scans newest-first, but the returned `items` are in **ascending**
  timestamp and message-ID order so the UI can render oldest-to-newest.
- The `olderBeforeMessageId` field is the **oldest item** on the returned
  page when the page is nonempty. Request that ID to page further backward
  only when `hasOlder` is true.

The JSON response uses the existing `RecentMessagePageResponse` shape:

```json
{
  "items": [],
  "olderBeforeMessageId": null,
  "hasOlder": false
}
```

Each item is a server-persisted `MessageResponse` with `messageId`,
`scopeType` (GROUP), `scopeId`, `senderUserId`, `clientMessageId`,
`body`, `replyToMessageId`, `createdUtc`, `editedUtc` and `deletedUtc`.

## Catch up after a SignalR disconnection

Use the **different** forward cursor contract:

```http
GET /api/v1/groups/{groupId}/messages?limit=100&afterMessageId={lastSeenMessageId}
Authorization: Bearer <current-session>
```

This returns a `MessagePageResponse`: `items`, `nextAfterMessageId`
and `hasMore`. Its database order is **ascending**, and it returns messages
strictly *after* the in-group cursor. Continue requesting pages while `hasMore`
and the cursor advances. A fresh or restored client should call the **recent**
endpoint first, not assume the first ascending page contains the newest messages.

`GroupMessageCreated` realtime events are hints, not durable history. The
browser merges messages by server `messageId`, replays the forward endpoint on
reconnect and retains permission checks on every HTTP request. No SignalR
subscription bypasses current membership authorization.

## Errors and security boundaries

| HTTP status | Observed meaning | Client response |
|---|---|---|
| 401 | Missing, expired or invalid bearer session | Ask user to reauthenticate |
| 403 | Authenticated caller lacks **current** group membership | Clear restricted group state; do **not** revoke unrelated account sessions |
| 404 | Unknown/foreign group-history cursor | Stop this page; refresh current membership and recent history |
| 200 | Authenticated and currently authorized page | Deduplicate on message ID, retain server order |

New group messages use `POST /api/v1/groups/{groupId}/messages` with a durable
client-generated `clientMessageId`. Reuse the same ID on retry of the **same**
content if the response was lost; do not invent a second message. The owner
checks membership and message-idempotency before broadcasting committed data.

**Limits:** This is owner-readable, server-authorized group messaging, **not
end-to-end encryption**. Current route authorization does not prove delivery
to offline clients. Build success and contract inspection do not replace a
live authorization, revocation, cursor and reconnect integration exercise.
