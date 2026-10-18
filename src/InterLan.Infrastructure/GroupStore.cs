using InterLan.Contracts;
using InterLan.Domain;
using Microsoft.Data.Sqlite;

namespace InterLan.Infrastructure;

public sealed class GroupStore(SqliteDatabase database)
{
    private static SqliteTransaction BeginWriteTransaction(SqliteConnection connection) =>
        connection.BeginTransaction(deferred: false);

    private static SqliteTransaction BeginReadTransaction(SqliteConnection connection) =>
        connection.BeginTransaction(deferred: true);

    public async Task<GroupDetailsResponse> CreateGroupAsync(
        Guid actorUserId,
        CreateGroupRequest request,
        CancellationToken cancellationToken = default)
    {
        var name = GroupTextPolicy.NormalizeName(request.Name);
        var topic = GroupTextPolicy.NormalizeTopic(request.Topic);

        await using var connection = database.OpenConnection();
        using var transaction = BeginWriteTransaction(connection);

        await RequireActiveUserAsync(
            connection,
            actorUserId,
            cancellationToken,
            transaction);

        var groupId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await using (var group = connection.CreateCommand())
        {
            group.Transaction = transaction;
            group.CommandText =
                """
                INSERT INTO groups (
                    group_id, name, topic, created_by_user_id, created_utc
                ) VALUES (
                    $groupId, $name, $topic, $creator, $createdUtc
                );
                """;
            group.Parameters.AddWithValue("$groupId", groupId.ToString("D"));
            group.Parameters.AddWithValue("$name", name);
            group.Parameters.AddWithValue(
                "$topic",
                topic is null ? DBNull.Value : topic);
            group.Parameters.AddWithValue("$creator", actorUserId.ToString("D"));
            group.Parameters.AddWithValue("$createdUtc", now.ToString("O"));
            await group.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var owner = connection.CreateCommand())
        {
            owner.Transaction = transaction;
            owner.CommandText =
                """
                INSERT INTO group_members (
                    group_id, user_id, group_role, joined_utc, removed_utc
                ) VALUES (
                    $groupId, $userId, 'OWNER', $joinedUtc, NULL
                );
                """;
            owner.Parameters.AddWithValue("$groupId", groupId.ToString("D"));
            owner.Parameters.AddWithValue("$userId", actorUserId.ToString("D"));
            owner.Parameters.AddWithValue("$joinedUtc", now.ToString("O"));
            await owner.ExecuteNonQueryAsync(cancellationToken);
        }

        await AppendGroupEventAsync(
            connection,
            transaction,
            groupId,
            actorUserId,
            actorUserId,
            "GROUP_CREATED",
            "{}",
            now,
            cancellationToken);

        await AppendAuditAsync(
            connection,
            transaction,
            actorUserId,
            "GROUP_CREATED",
            groupId,
            "{}",
            now,
            cancellationToken);

        var created = await GetGroupDetailsAsync(
            connection,
            transaction,
            actorUserId,
            groupId,
            cancellationToken);

        transaction.Commit();
        return created;
    }

    public async Task<IReadOnlyList<GroupSummaryResponse>> ListGroupsAsync(
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = database.OpenConnection();
        using var transaction = BeginReadTransaction(connection);

        await RequireActiveUserAsync(
            connection,
            actorUserId,
            cancellationToken,
            transaction);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT g.group_id, g.name, g.topic,
                   gm.group_role, g.created_utc
            FROM groups g
            JOIN group_members gm
              ON gm.group_id = g.group_id
             AND gm.user_id = $userId
             AND gm.removed_utc IS NULL
            ORDER BY lower(g.name), g.group_id;
            """;
        command.Parameters.AddWithValue("$userId", actorUserId.ToString("D"));

        var groups = new List<GroupSummaryResponse>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            groups.Add(new GroupSummaryResponse(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3),
                DateTimeOffset.Parse(reader.GetString(4))));
        }

        await reader.DisposeAsync();
        transaction.Commit();
        return groups;
    }

    public async Task<GroupDetailsResponse> GetGroupDetailsAsync(
        Guid actorUserId,
        Guid groupId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = database.OpenConnection();
        using var transaction = BeginReadTransaction(connection);

        var details = await GetGroupDetailsAsync(
            connection,
            transaction,
            actorUserId,
            groupId,
            cancellationToken);

        transaction.Commit();
        return details;
    }

    private static async Task<GroupDetailsResponse> GetGroupDetailsAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        Guid actorUserId,
        Guid groupId,
        CancellationToken cancellationToken)
    {
        var myRole = await RequireActiveGroupMemberAsync(
            connection,
            actorUserId,
            groupId,
            cancellationToken,
            transaction);

        string name;
        string? topic;
        Guid creator;
        DateTimeOffset createdUtc;

        await using (var group = connection.CreateCommand())
        {
            group.Transaction = transaction;
            group.CommandText =
                """
                SELECT name, topic, created_by_user_id, created_utc
                FROM groups
                WHERE group_id = $groupId;
                """;
            group.Parameters.AddWithValue("$groupId", groupId.ToString("D"));

            await using var reader = await group.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new KeyNotFoundException("Group not found.");

            name = reader.GetString(0);
            topic = reader.IsDBNull(1) ? null : reader.GetString(1);
            creator = Guid.Parse(reader.GetString(2));
            createdUtc = DateTimeOffset.Parse(reader.GetString(3));
        }

        var members = new List<GroupMemberResponse>();
        await using (var member = connection.CreateCommand())
        {
            member.Transaction = transaction;
            member.CommandText =
                """
                SELECT u.user_id, u.username, u.display_name,
                       gm.group_role, gm.joined_utc
                FROM group_members gm
                JOIN users u ON u.user_id = gm.user_id
                WHERE gm.group_id = $groupId
                  AND gm.removed_utc IS NULL
                  AND u.disabled_utc IS NULL
                ORDER BY
                    CASE gm.group_role
                        WHEN 'OWNER' THEN 0
                        WHEN 'ADMIN' THEN 1
                        ELSE 2
                    END,
                    lower(u.display_name),
                    u.user_id;
                """;
            member.Parameters.AddWithValue("$groupId", groupId.ToString("D"));

            await using var reader = await member.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                members.Add(new GroupMemberResponse(
                    Guid.Parse(reader.GetString(0)),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    DateTimeOffset.Parse(reader.GetString(4))));
            }
        }

        return new GroupDetailsResponse(
            groupId,
            name,
            topic,
            creator,
            createdUtc,
            myRole,
            members);
    }

    public async Task<GroupDetailsResponse> UpdateGroupAsync(
        Guid actorUserId,
        Guid groupId,
        UpdateGroupRequest request,
        CancellationToken cancellationToken = default)
    {
        var name = GroupTextPolicy.NormalizeName(request.Name);
        var topic = GroupTextPolicy.NormalizeTopic(request.Topic);

        await using var connection = database.OpenConnection();
        using var transaction = BeginWriteTransaction(connection);

        var role = await RequireActiveGroupMemberAsync(
            connection,
            actorUserId,
            groupId,
            cancellationToken,
            transaction);

        GroupRolePolicy.RequireMetadataEditor(role);

        var now = DateTimeOffset.UtcNow;

        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText =
                """
                UPDATE groups
                SET name = $name,
                    topic = $topic
                WHERE group_id = $groupId;
                """;
            update.Parameters.AddWithValue("$name", name);
            update.Parameters.AddWithValue(
                "$topic",
                topic is null ? DBNull.Value : topic);
            update.Parameters.AddWithValue("$groupId", groupId.ToString("D"));

            if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new KeyNotFoundException("Group not found.");
        }

        await AppendGroupEventAsync(
            connection,
            transaction,
            groupId,
            actorUserId,
            null,
            "GROUP_METADATA_UPDATED",
            "{}",
            now,
            cancellationToken);

        await AppendAuditAsync(
            connection,
            transaction,
            actorUserId,
            "GROUP_METADATA_UPDATED",
            groupId,
            "{}",
            now,
            cancellationToken);

        var updated = await GetGroupDetailsAsync(
            connection,
            transaction,
            actorUserId,
            groupId,
            cancellationToken);

        transaction.Commit();
        return updated;
    }

    public async Task<GroupMembershipMutationResponse> AddMemberAsync(
        Guid actorUserId,
        Guid groupId,
        AddGroupMemberRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.UserId == Guid.Empty)
            throw new ArgumentException("Group member user ID is required.", nameof(request));

        var requestedRole = GroupRolePolicy.NormalizeAssignableRole(request.Role);

        await using var connection = database.OpenConnection();
        using var transaction = BeginWriteTransaction(connection);

        await RequireActiveUserAsync(
            connection,
            request.UserId,
            cancellationToken,
            transaction);

        var actorRole = await RequireActiveGroupMemberAsync(
            connection,
            actorUserId,
            groupId,
            cancellationToken,
            transaction);

        GroupRolePolicy.RequireCanAdd(actorRole, requestedRole);

        var existing = await GetGroupMemberStateAsync(
            connection,
            groupId,
            request.UserId,
            cancellationToken,
            transaction);

        if (existing is { RemovedUtc: null })
        {
            if (existing.Value.Role == requestedRole)
            {
                transaction.Commit();
                return new GroupMembershipMutationResponse(
                    groupId,
                    request.UserId,
                    requestedRole,
                    "UNCHANGED",
                    DateTimeOffset.UtcNow);
            }

            throw new InvalidOperationException(
                "User is already an active group member. Use the role mutation endpoint.");
        }

        var now = DateTimeOffset.UtcNow;

        if (existing is null)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText =
                """
                INSERT INTO group_members (
                    group_id, user_id, group_role, joined_utc, removed_utc
                ) VALUES (
                    $groupId, $userId, $role, $joinedUtc, NULL
                );
                """;
            insert.Parameters.AddWithValue("$groupId", groupId.ToString("D"));
            insert.Parameters.AddWithValue("$userId", request.UserId.ToString("D"));
            insert.Parameters.AddWithValue("$role", requestedRole);
            insert.Parameters.AddWithValue("$joinedUtc", now.ToString("O"));
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        else
        {
            await using var restore = connection.CreateCommand();
            restore.Transaction = transaction;
            restore.CommandText =
                """
                UPDATE group_members
                SET group_role = $role,
                    joined_utc = $joinedUtc,
                    removed_utc = NULL
                WHERE group_id = $groupId
                  AND user_id = $userId
                  AND removed_utc IS NOT NULL;
                """;
            restore.Parameters.AddWithValue("$role", requestedRole);
            restore.Parameters.AddWithValue("$joinedUtc", now.ToString("O"));
            restore.Parameters.AddWithValue("$groupId", groupId.ToString("D"));
            restore.Parameters.AddWithValue("$userId", request.UserId.ToString("D"));

            if (await restore.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidOperationException("Removed group membership could not be restored.");
        }

        var payload =
            $"{{\"userId\":\"{request.UserId:D}\",\"role\":\"{requestedRole}\"}}";

        await AppendGroupEventAsync(
            connection,
            transaction,
            groupId,
            actorUserId,
            request.UserId,
            "MEMBER_ADDED",
            payload,
            now,
            cancellationToken);

        await AppendAuditAsync(
            connection,
            transaction,
            actorUserId,
            "GROUP_MEMBER_ADDED",
            groupId,
            payload,
            now,
            cancellationToken);

        transaction.Commit();

        return new GroupMembershipMutationResponse(
            groupId,
            request.UserId,
            requestedRole,
            "ADDED",
            now);
    }

    public async Task<GroupMembershipMutationResponse> RemoveMemberAsync(
        Guid actorUserId,
        Guid groupId,
        Guid targetUserId,
        CancellationToken cancellationToken = default)
    {
        if (targetUserId == Guid.Empty)
            throw new ArgumentException("Group member user ID is required.", nameof(targetUserId));

        await using var connection = database.OpenConnection();
        using var transaction = BeginWriteTransaction(connection);

        var actorRole = await RequireActiveGroupMemberAsync(
            connection,
            actorUserId,
            groupId,
            cancellationToken,
            transaction);

        var target = await GetGroupMemberStateAsync(
            connection,
            groupId,
            targetUserId,
            cancellationToken,
            transaction);

        if (target is null || target.Value.RemovedUtc is not null)
            throw new KeyNotFoundException("Active group member was not found.");

        GroupRolePolicy.RequireCanRemove(
            actorRole,
            target.Value.Role,
            actorUserId == targetUserId);

        var now = DateTimeOffset.UtcNow;

        await using (var remove = connection.CreateCommand())
        {
            remove.Transaction = transaction;
            remove.CommandText =
                """
                UPDATE group_members
                SET removed_utc = $removedUtc
                WHERE group_id = $groupId
                  AND user_id = $userId
                  AND removed_utc IS NULL;
                """;
            remove.Parameters.AddWithValue("$removedUtc", now.ToString("O"));
            remove.Parameters.AddWithValue("$groupId", groupId.ToString("D"));
            remove.Parameters.AddWithValue("$userId", targetUserId.ToString("D"));

            if (await remove.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidOperationException("Group membership removal lost its authority race.");
        }

        var payload =
            $"{{\"userId\":\"{targetUserId:D}\",\"previousRole\":\"{target.Value.Role}\"}}";

        await AppendGroupEventAsync(
            connection,
            transaction,
            groupId,
            actorUserId,
            targetUserId,
            "MEMBER_REMOVED",
            payload,
            now,
            cancellationToken);

        await AppendAuditAsync(
            connection,
            transaction,
            actorUserId,
            "GROUP_MEMBER_REMOVED",
            groupId,
            payload,
            now,
            cancellationToken);

        transaction.Commit();

        return new GroupMembershipMutationResponse(
            groupId,
            targetUserId,
            null,
            "REMOVED",
            now);
    }

    public async Task<GroupMembershipMutationResponse> UpdateMemberRoleAsync(
        Guid actorUserId,
        Guid groupId,
        Guid targetUserId,
        UpdateGroupMemberRoleRequest request,
        CancellationToken cancellationToken = default)
    {
        if (targetUserId == Guid.Empty)
            throw new ArgumentException("Group member user ID is required.", nameof(targetUserId));

        var requestedRole = GroupRolePolicy.NormalizeAssignableRole(request.Role);

        await using var connection = database.OpenConnection();
        using var transaction = BeginWriteTransaction(connection);

        var actorRole = await RequireActiveGroupMemberAsync(
            connection,
            actorUserId,
            groupId,
            cancellationToken,
            transaction);

        GroupRolePolicy.RequireOwner(actorRole);

        var target = await GetGroupMemberStateAsync(
            connection,
            groupId,
            targetUserId,
            cancellationToken,
            transaction);

        if (target is null || target.Value.RemovedUtc is not null)
            throw new KeyNotFoundException("Active group member was not found.");

        GroupRolePolicy.RequireMutableTarget(target.Value.Role);

        if (target.Value.Role == requestedRole)
        {
            transaction.Commit();
            return new GroupMembershipMutationResponse(
                groupId,
                targetUserId,
                requestedRole,
                "UNCHANGED",
                DateTimeOffset.UtcNow);
        }

        var previousRole = target.Value.Role;
        var now = DateTimeOffset.UtcNow;

        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText =
                """
                UPDATE group_members
                SET group_role = $role
                WHERE group_id = $groupId
                  AND user_id = $userId
                  AND removed_utc IS NULL
                  AND group_role <> 'OWNER';
                """;
            update.Parameters.AddWithValue("$role", requestedRole);
            update.Parameters.AddWithValue("$groupId", groupId.ToString("D"));
            update.Parameters.AddWithValue("$userId", targetUserId.ToString("D"));

            if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidOperationException("Group role mutation lost its authority race.");
        }

        var payload =
            $"{{\"userId\":\"{targetUserId:D}\",\"previousRole\":\"{previousRole}\",\"role\":\"{requestedRole}\"}}";

        await AppendGroupEventAsync(
            connection,
            transaction,
            groupId,
            actorUserId,
            targetUserId,
            "MEMBER_ROLE_CHANGED",
            payload,
            now,
            cancellationToken);

        await AppendAuditAsync(
            connection,
            transaction,
            actorUserId,
            "GROUP_MEMBER_ROLE_CHANGED",
            groupId,
            payload,
            now,
            cancellationToken);

        transaction.Commit();

        return new GroupMembershipMutationResponse(
            groupId,
            targetUserId,
            requestedRole,
            "ROLE_CHANGED",
            now);
    }

    public async Task<IReadOnlyList<GroupEventResponse>> ListGroupEventsAsync(
        Guid actorUserId,
        Guid groupId,
        Guid? afterEventId = null,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 250)
            throw new ArgumentOutOfRangeException(nameof(limit));

        await using var connection = database.OpenConnection();
        using var transaction = BeginReadTransaction(connection);

        await RequireActiveGroupMemberAsync(
            connection,
            actorUserId,
            groupId,
            cancellationToken,
            transaction);

        string? afterCreatedUtc = null;
        string? afterId = null;

        if (afterEventId is { } cursor)
        {
            await using var cursorCommand = connection.CreateCommand();
            cursorCommand.Transaction = transaction;
            cursorCommand.CommandText =
                """
                SELECT created_utc, group_event_id
                FROM group_events
                WHERE group_event_id = $eventId
                  AND group_id = $groupId;
                """;
            cursorCommand.Parameters.AddWithValue("$eventId", cursor.ToString("D"));
            cursorCommand.Parameters.AddWithValue("$groupId", groupId.ToString("D"));

            await using var reader =
                await cursorCommand.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new KeyNotFoundException(
                    "Group event cursor was not found.");

            afterCreatedUtc = reader.GetString(0);
            afterId = reader.GetString(1);
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT group_event_id, group_id, actor_user_id,
                   subject_user_id, event_type, payload_json, created_utc
            FROM group_events
            WHERE group_id = $groupId
              AND (
                  $afterCreatedUtc IS NULL
                  OR created_utc > $afterCreatedUtc
                  OR (created_utc = $afterCreatedUtc AND group_event_id > $afterId)
              )
            ORDER BY created_utc, group_event_id
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$groupId", groupId.ToString("D"));
        command.Parameters.AddWithValue(
            "$afterCreatedUtc",
            afterCreatedUtc is null ? DBNull.Value : afterCreatedUtc);
        command.Parameters.AddWithValue(
            "$afterId",
            afterId is null ? DBNull.Value : afterId);
        command.Parameters.AddWithValue("$limit", limit);

        var events = new List<GroupEventResponse>();
        await using var eventReader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await eventReader.ReadAsync(cancellationToken))
        {
            events.Add(new GroupEventResponse(
                Guid.Parse(eventReader.GetString(0)),
                Guid.Parse(eventReader.GetString(1)),
                eventReader.IsDBNull(2)
                    ? null
                    : Guid.Parse(eventReader.GetString(2)),
                eventReader.IsDBNull(3)
                    ? null
                    : Guid.Parse(eventReader.GetString(3)),
                eventReader.GetString(4),
                eventReader.GetString(5),
                DateTimeOffset.Parse(eventReader.GetString(6))));
        }

        await eventReader.DisposeAsync();
        transaction.Commit();
        return events;
    }

    private static async Task<(string Role, DateTimeOffset? RemovedUtc)?> GetGroupMemberStateAsync(
        SqliteConnection connection,
        Guid groupId,
        Guid userId,
        CancellationToken cancellationToken,
        SqliteTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT group_role, removed_utc
            FROM group_members
            WHERE group_id = $groupId
              AND user_id = $userId;
            """;
        command.Parameters.AddWithValue("$groupId", groupId.ToString("D"));
        command.Parameters.AddWithValue("$userId", userId.ToString("D"));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return (
            reader.GetString(0),
            reader.IsDBNull(1)
                ? null
                : DateTimeOffset.Parse(reader.GetString(1)));
    }

    private static async Task AppendGroupEventAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid groupId,
        Guid? actorUserId,
        Guid? subjectUserId,
        string eventType,
        string payloadJson,
        DateTimeOffset createdUtc,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO group_events (
                group_event_id, group_id, actor_user_id,
                subject_user_id, event_type, payload_json, created_utc
            ) VALUES (
                $eventId, $groupId, $actorUserId,
                $subjectUserId, $eventType, $payloadJson, $createdUtc
            );
            """;
        command.Parameters.AddWithValue("$eventId", Guid.NewGuid().ToString("D"));
        command.Parameters.AddWithValue("$groupId", groupId.ToString("D"));
        command.Parameters.AddWithValue(
            "$actorUserId",
            actorUserId is null ? DBNull.Value : actorUserId.Value.ToString("D"));
        command.Parameters.AddWithValue(
            "$subjectUserId",
            subjectUserId is null ? DBNull.Value : subjectUserId.Value.ToString("D"));
        command.Parameters.AddWithValue("$eventType", eventType);
        command.Parameters.AddWithValue("$payloadJson", payloadJson);
        command.Parameters.AddWithValue("$createdUtc", createdUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task AppendAuditAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid actorUserId,
        string eventType,
        Guid groupId,
        string payloadJson,
        DateTimeOffset createdUtc,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO audit_events (
                audit_event_id, actor_user_id, event_type,
                subject_type, subject_id, payload_json, created_utc
            ) VALUES (
                $auditId, $actorUserId, $eventType,
                'GROUP', $groupId, $payloadJson, $createdUtc
            );
            """;
        command.Parameters.AddWithValue("$auditId", Guid.NewGuid().ToString("D"));
        command.Parameters.AddWithValue("$actorUserId", actorUserId.ToString("D"));
        command.Parameters.AddWithValue("$eventType", eventType);
        command.Parameters.AddWithValue("$groupId", groupId.ToString("D"));
        command.Parameters.AddWithValue("$payloadJson", payloadJson);
        command.Parameters.AddWithValue("$createdUtc", createdUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task RequireActiveUserAsync(
        SqliteConnection connection,
        Guid userId,
        CancellationToken cancellationToken,
        SqliteTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT COUNT(1)
            FROM users
            WHERE user_id = $userId
              AND disabled_utc IS NULL;
            """;
        command.Parameters.AddWithValue("$userId", userId.ToString("D"));

        if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) != 1)
            throw new UnauthorizedAccessException("Active user is required.");
    }

    private static async Task<string> RequireActiveGroupMemberAsync(
        SqliteConnection connection,
        Guid actorUserId,
        Guid groupId,
        CancellationToken cancellationToken,
        SqliteTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT gm.group_role
            FROM group_members gm
            JOIN users u ON u.user_id = gm.user_id
            WHERE gm.group_id = $groupId
              AND gm.user_id = $userId
              AND gm.removed_utc IS NULL
              AND u.disabled_utc IS NULL;
            """;
        command.Parameters.AddWithValue("$groupId", groupId.ToString("D"));
        command.Parameters.AddWithValue("$userId", actorUserId.ToString("D"));

        var role = Convert.ToString(
            await command.ExecuteScalarAsync(cancellationToken));

        return !string.IsNullOrWhiteSpace(role)
            ? role
            : throw new UnauthorizedAccessException("Active group membership is required.");
    }
}
