# Authorized attachment transfer contract (FILES-001)

**Implemented source:** server storage and routes merged by PR #107; scoped history listing by #137; browser group/direct file workspace by #143 and #150. A successful merge/build is not evidence of a completed production deployment. The authoritative implementation lives in `src/InterLan.Server/AttachmentEndpointMappings.cs`, `src/InterLan.Infrastructure/AttachmentStore.cs`, `src/InterLan.Contracts/ChatContracts.cs`, and migration `009_authorized_attachment_scopes.sql`.

## HTTPS authentication and scope

All routes require a valid server-issued session bearer. Owner/process status and client-provided user IDs do not confer file permissions. File access is allowed only for a **currently enabled user** with current membership in the exact DIRECT conversation or active GROUP. Revoked or removed members cannot create new uploads, retrieve metadata or begin downloads. The existing session-validation layer remains authoritative.

**Upload a group attachment:**

```http
POST /api/v1/groups/{groupId}/attachments
Authorization: Bearer <approved-session-token>
Content-Type: application/octet-stream
X-InterLan-Filename: report.pdf

<binary request body>
```

For direct messages, use `POST /api/v1/direct/{conversationId}/attachments` with the same body contract. Neither URI takes a local server path. The supplied filename is display metadata only; path separators are removed before storage. The server generates an unpredictable attachment ID, writes bytes under server-owned directories, and computes SHA-256 on the streamed bytes.

**Read metadata:**

`GET /api/v1/attachments/{attachmentId}/metadata`

This returns an `AttachmentResponse` with `attachmentId`, `scopeType`, `scopeId`, `uploaderUserId`, `originalFilename`, `mimeType`, `byteSize`, `sha256`, `createdUtc` and `archiveState`. Only presently authorized members can retrieve it. Legacy attachment rows without verified scope remain inaccessible.

**List files in an authorized conversation:**

```http
GET /api/v1/groups/{groupId}/attachments?limit=50
Authorization: Bearer <active-session>
```

Direct conversations use `GET /api/v1/direct/{conversationId}/attachments`
with the same query contract. The list defaults to 50, with a server-clamped
page limit of 1–100. A page is an `AttachmentPageResponse` with
`items`, `nextBeforeAttachmentId` and `hasMore`.
Items are ordered newest-first by `createdUtc, attachmentId`.
When `hasMore` is true, pass the previous page's
`nextBeforeAttachmentId` in `beforeAttachmentId` to retrieve older files.
The server rejects unknown/foreign scope cursors with 404 and checks active
membership inside the same SQLite read transaction as list retrieval.
No unscoped legacy attachments are surfaced by this API.

The current browser Files page offers group or direct-conversation selection,
scope-specific upload and metadata listing, bounded pages, and authorized
downloads. Client-side authorization is not trusted. The browser uses a
server-supplied upload policy, sanitizes inert filename headers, caps
download streaming memory, verifies each SHA-256 before saving, and never
persists session credentials in a URL.

**Download:**

`GET /api/v1/attachments/{attachmentId}/content`

Downloads are forced as `application/octet-stream` attachments rather than executing user-provided MIME/HTML inline. The server resolves the blob path exclusively from the server-minted attachment ID. A successfully opened download is a stream; revocation does not retroactively erase bytes already transmitted before the membership change.

**Policy:**

`GET /api/v1/attachments/policy` returns the maximum upload bytes, quota per conversation/group and upload protocol. The first implementation allows up to 10 MiB per upload and 100 MiB per scope. These are server bounds, not browser-provided configuration.

## Persistence and recovery

The server writes the request body into a private randomly named staging file, enforces the upload limit while streaming and calculates the SHA-256. It rechecks current scope membership in a serialized SQLite writer transaction, checks the scope quota, renames the completed staging file into server-owned storage, then commits metadata. Cancellation and failed transactions remove their staging/final files. An abrupt process termination between rename and database commit may leave an **unreferenced orphan blob**; no metadata endpoint can authorize that blob. On canonical owner-server startup, before accepting requests, recovery deletes abandoned exact-UUID staging files and exact-UUID blob files without a matching committed attachment metadata reference. Referenced blobs are preserved. **Startup recovery and durable fsync/restart behavior have NOT been runtime-verified.**

Migration 009 **adds** nullable `scope_type` and `scope_id` to the existing attachments table and does not rewrite old attachment rows or claim historical group permission. An old unscoped record cannot be downloaded through the new routes.

## HTTP outcomes and limitations

- Unauthenticated or invalid session: **401**.
- Currently unauthorized membership: **403**.
- Unknown/missing attachment or wrong cursor/scope lookup: **404** where applicable.
- Invalid display filename, MIME or empty payload: **400**.
- Declared **or streamed** payload above the 10 MiB upload cap: **413** (PR #147).
- Scope quota exceeded: **409**.

The server and browser implementations are merged, and their applicable
P0/P1/P2 source-head GitHub workflows passed in the cited PRs. That **does not**
establish a completed production environment, a manual upload/revoke/restart
matrix, or disaster recovery proof. The existing file slice does not attach
a file to a sent message (`message_id` is initially null), automatically
archive to Telegram, deduplicate identical blobs, or provide resumable
uploads. `archiveState=PENDING` is schema state, **not proof** an archival
worker exists. Desktop/native file UX remains a separate scope.

**Security acceptance:** test revocation, HTTP streaming cancellation, unauthorized group/direct access, quota races, crash restoration, migration upgrades and MIME/download behavior before enabling production transfer.
