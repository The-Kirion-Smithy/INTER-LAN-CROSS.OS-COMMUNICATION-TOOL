# INTER-LAN database: implemented SQLite model and migration authority

Source: `src/InterLan.Infrastructure/SqliteDatabase.cs`, `Migrations/001_initial.sql` through `008_p3_group_authority.sql`, `EnrollmentStore.cs`, `ChatStore.cs`, `GroupStore.cs` and `ServerSettingsStore.cs`. This document catalogs **source-backed** state; a declared schema/table alone is not evidence that all product workflows exist.

## Database ownership and durability

One owner-hosted server process opens an SQLite database at **`<data-directory>/interlan.db`**. `InterLanServerHost.BuildAsync` resolves a configured data directory, constructs `SqliteDatabase`, initializes schema, loads persisted server settings, and configures the HTTPS listener with the server's local certificate identity.

`SqliteDatabase.OpenConnection` creates the enclosing directory, opens a shared-cache read/write/create connection and applies `PRAGMA foreign_keys=ON`, `PRAGMA busy_timeout=5000`, and `PRAGMA synchronous=NORMAL`. Initialization enables **WAL journal mode** and stops startup if WAL cannot be activated.

WAL matters operationally: active data may exist in `interlan.db-wal`, not yet in the main file. **Never copy only `interlan.db` while the server is running and call that a consistent backup.** See [backup and restore playbook](operations/SQLITE_TLS_RECOVERY.md).

## Migration ledger, ordering and failure policy

`InitializeAsync` creates `schema_migrations(migration_id, applied_utc)`, reads embedded `*.Migrations.*.sql` assembly resources, orders them ordinally and executes each not-yet-recorded SQL migration **inside its own transaction**. Only after SQL completes does the same transaction record the migration ID. An already-recorded migration is skipped on subsequent startup.

Consequences:
- Do not rename/reorder or edit accepted SQL migrations in place. Implement new schema changes through new ordinal migration files.
- A failed migration should roll back its transaction and prevent startup rather than pretending that the database is up-to-date.
- The table records *which* migration ID was applied, not a cryptographic checksum of migration contents. Code review and accepted Git history are the migration-change authority.
- For existing instances, backups of the complete owner state are required **before** new migrations; rollback may require restoring the backup rather than running an unsupported reverse migration.

## Applied schema progression

| Migration | Actual durable change |
|---|---|
| `001_initial.sql` | Users and singleton server owner/settings; devices and invites; direct/group conversation/member tables; scope-typed messages, receipts, attachment/archive job placeholders and audit events |
| `002_p1_identity_enrollment.sql` | Owner user password salt/hash/iteration columns, join requests with status/secret hash, device session tokens with expiry/revocation |
| `003_p2_direct_messages.sql` | Direct-conversation pair key and uniqueness; membership and message-cursor indexes |
| `004_p2_device_pairing_persistence.sql` | Device credential hash plus uniqueness index |
| `005_p2_device_credential_lifecycle.sql` | Credential created/rotated/last-used timestamps |
| `006_p2_query_indexes.sql` | Supporting indexes for conversation members, message order, receipts, device revocation and active sessions |
| `007_p2_direct_preferences.sql` | Per-conversation, per-user pin, mute, archive and update timestamps |
| `008_p3_group_authority.sql` | Durable `group_events` history, group event cursor and active membership indexes |

At the time of this source audit, **008 is the highest committed schema migration**. A new feature needing persistent data must allocate a new additive migration; never invent a schema outside the migration system.

## Identity, owner authority, revocation

`users` holds account identity and disabled state. `server_identity` is a singleton with `singleton_key=1`, server ID/name and **explicit owner_user_id**. Server-wide OWNER/ADMIN/MEMBER roles are not equivalent to group-local roles.

`invite_tokens` stores hashes and expiry/consumption; `join_requests` captures enrollment-secret hashes, requested identity/device data, decisions and whether approval was exchanged. `devices` records device approval/revocation and hashed device renewal credential metadata. `device_sessions` records hashed bearer tokens, expiry and revocation. Authorization code, not the UI or process owner, must verify active session/device state before privileged reads/mutations.

A database backup contains sensitive authentication material even though tokens are hashed. Treat it as a high-value security asset.

## Direct conversations and message integrity

- `direct_conversations` holds conversation IDs and an optional unique `pair_key`; `direct_conversation_members` identifies authorized participants.
- `messages` holds scoped messages with `scope_type` DIRECT or GROUP, scope ID, sender, client message ID, reply reference, timestamps and soft edit/delete metadata. `UNIQUE(sender_user_id, client_message_id)` supplies a durable idempotency boundary. Do not use this key across unrelated actions without understanding that scope.
- `message_receipts` stores per-message delivered/read state. A realtime notification does not itself prove these rows were persisted.
- `direct_conversation_preferences` stores each user's pin, archive and mute state, separate from membership and messages.
- Indexes cover scope ordering and cursor retrieval; consumers must maintain stable cursor identifiers rather than arbitrary timestamp offsets.

The implementation work for direct-message operations is primarily in `ChatStore.cs`, `MessagingEndpointMappings.cs` and `Realtime/ChatHub.cs`.

## Groups, role transitions and event audit

`groups` and `group_members` originated in migration 001. The `group_role` constraint permits OWNER/ADMIN/MEMBER; `removed_utc` distinguishes active from removed membership. Migration 008 adds `group_events` with event ID, group, actor/subject user, event type, JSON payload and creation timestamp, along with ordered group event and active-member indexes.

`GroupStore.cs` performs group creation, list/details, metadata changes, membership add/restore/remove, role changes and event reads through SQL transactions. `GroupRolePolicy.cs` controls who may change those rows. The **database cannot replace authorization**: every API mutation must derive actor identity from a validated server session.

Group authority persistence is integrated; durable **group message** flows and strict realtime membership-removal cutoffs remain separate implementation work ([issue #35](https://github.com/The-Kirion-Smithy/INTER-LAN-CROSS.OS-COMMUNICATION-TOOL/issues/35)).

## Attachment and archive schema is not feature acceptance

The first migration declares `attachments`, `telegram_archives`, `archive_jobs` and associated indexes. These reserve durable data structures, but **do not claim** that authenticated uploads/downloads, Telegram credential isolation, reliable archive workers, retry semantics or file retention have been completed because those tables exist.

## Data-handling invariants

1. Only the canonical owner server writes its authoritative SQLite state; browser/native clients call authenticated HTTP/realtime interfaces.
2. Preserve `interlan.db`, associated WAL data and **both** TLS identity files as one server recovery boundary. Never re-create just one cert/key file.
3. Keep the data directory off public shares and source control. Protect file permissions and encrypted backups.
4. Schema and runtime changes require issue/PR provenance. An accepted commit SHA is evidence of source history, **not** a successful migration execution.
5. The current code-first campaign does not run new tests/CI by request; no data-recovery or concurrency acceptance is implied here.
