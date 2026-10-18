# P3 group membership and role HTTP contract

**Source baseline:** group foundation merged in PR #33. Source: `src/InterLan.Server/GroupEndpointMappings.cs`, `src/InterLan.Infrastructure/GroupStore.cs`, `src/InterLan.Domain/GroupRolePolicy.cs`, `src/InterLan.Contracts/ChatContracts.cs` and migration `008_p3_group_authority.sql`.

## Model and authority
Group creator is assigned the group's canonical OWNER role within a durable server transaction. Group roles are `OWNER`, `ADMIN`, and `MEMBER`; they are **distinct from** server-wide account roles. Access to group contents must be based on current, active group membership. Never assume that a client-side hidden button enforces permissions.

The current policy exposes these rules:
- OWNER or ADMIN may change group metadata and add regular members.
- Only OWNER may appoint an ADMIN and mutate existing member roles.
- ADMIN cannot remove another ADMIN.
- A group OWNER cannot be silently removed, demoted or transferred.
- A member may leave where removal policy permits; management by another member requires appropriate privilege.
- A disabled user should not be exposed as an active group member in response projections.

## HTTP routes from current source

| Verb | Route | Intent |
|---|---|---|
| POST | `/api/v1/groups` | Create server-controlled group; body `CreateGroupRequest` |
| GET | `/api/v1/groups` | List groups visible to signed-in member |
| GET | `/api/v1/groups/{groupId}` | Member-only details, current role and member list |
| PUT | `/api/v1/groups/{groupId}` | Update name/topic under OWNER/ADMIN policy |
| GET | `/api/v1/groups/{groupId}/events` | Member-only audit timeline; `afterEventId` and bounded `limit` |
| POST | `/api/v1/groups/{groupId}/members` | Add or restore membership subject to role policy |
| DELETE | `/api/v1/groups/{groupId}/members/{userId}` | Remove/leave if permitted |
| PUT | `/api/v1/groups/{groupId}/members/{userId}/role` | OWNER-controlled role change |

Routes resolve the authenticated principal through `AuthorizationHelpers.GetPrincipalAsync`. The server computes the actor user ID from the validated session; callers must never submit a privileged `actorUserId` as identity authority.

## Request behavior
Create: `{"name":"Operations","topic":"Office notices"}`.
Update: `{"name":"Operations","topic":null}`.
Add member: `{"userId":"<valid-user-guid>","role":"MEMBER"}` or `"ADMIN"`.
Change role: `{"role":"MEMBER"}` / `"ADMIN"`. OWNER is not directly assignable via normal role-mutation request.

The name and topic are normalized by `GroupTextPolicy`. Do not invent client-side-only validation as a substitute for server validation.

## Response/error semantics
- `401`: missing/invalid session.
- `403`: caller lacks active membership or required role on routes handling this explicitly.
- `404`: group or target resource not found on routes handling this explicitly.
- `400`: invalid request data on routes handling validation exceptions.
- `409`: conflicting membership state or prohibited transition on mutation routes.
- `200`: corresponding mutation/list/details returned successfully.

**Important:** these are current route-specific mappings, not a promise that every exception is normalized on every route. Error handling deserves further source review and validation before treating this as a stable public SDK contract.

## Durable audit events and migration
The pre-existing `groups` and `group_members` tables originate in the initial SQLite schema. Migration 008 adds `group_events` and active-membership query indexes. Membership and metadata operations append audit records as server-side transaction work. `GET /events` bounds the requested limit to 1–250 and paginates by `afterEventId`; clients must preserve the returned cursor rather than inventing timestamp offsets.

## Integration boundaries
The route set here is **group identity/metadata/membership**, not proof that durable group message posting or realtime group fan-out has been accepted. The latter is tracked separately under issue #35. Browser UI issue #36 and native UI issue #37 should consume real accepted server routes and treat 403 responses as authoritative. No tests or CI were run for this documentation.
