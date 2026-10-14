using InterLan.Contracts;
using InterLan.Domain;
using Microsoft.Data.Sqlite;

namespace InterLan.Infrastructure;

public sealed class ChatStore(SqliteDatabase database)
{
    public const int MaxMessageLength = 4_000;
    public const int MaxMessagePageSize = 200;

    public async Task<IReadOnlyList<UserSummaryResponse>> ListUsersAsync(
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = database.OpenConnection();
        await RequireActiveUserAsync(connection, actorUserId, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT user_id, username, display_name, role
            FROM users
            WHERE disabled_utc IS NULL
            ORDER BY lower(display_name), lower(username), user_id;
            """;

        var users = new List<UserSummaryResponse>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            users.Add(new UserSummaryResponse(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3)));
        }

        return users;
    }

    public async Task<UserDirectorySearchResponse> SearchUsersAsync(
        Guid actorUserId,
        string query,
        int limit = 25,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = (query ?? string.Empty)
            .Trim()
            .Normalize(System.Text.NormalizationForm.FormC);

        if (normalizedQuery.Length is < 1 or > 64)
            throw new ArgumentException(
                "User search query must contain between 1 and 64 characters.",
                nameof(query));

        if (limit is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(limit));

        await using var connection = database.OpenConnection();
        await RequireActiveUserAsync(connection, actorUserId, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT user_id, username, display_name, role
            FROM users
            WHERE disabled_utc IS NULL
              AND user_id <> $actor
              AND (
                  instr(lower(username), lower($query)) > 0
                  OR instr(lower(display_name), lower($query)) > 0
              )
            ORDER BY lower(display_name), lower(username), user_id
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$actor", actorUserId.ToString("D"));
        command.Parameters.AddWithValue("$query", normalizedQuery);
        command.Parameters.AddWithValue("$limit", limit);

        var users = new List<UserSummaryResponse>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            users.Add(new UserSummaryResponse(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3)));
        }

        return new UserDirectorySearchResponse(
            normalizedQuery,
            users);
    }

    public async Task<DirectConversationResponse> GetOrCreateDirectConversationAsync(
        Guid actorUserId,
        Guid otherUserId,
        CancellationToken cancellationToken = default)
    {
        if (actorUserId == Guid.Empty || otherUserId == Guid.Empty)
            throw new ArgumentException("User IDs must be non-empty.");
        if (actorUserId == otherUserId)
            throw new ArgumentException("A direct conversation requires two distinct users.");

        await using var connection = database.OpenConnection();
        await RequireActiveUserAsync(connection, actorUserId, cancellationToken);
        var other = await GetActiveUserAsync(connection, otherUserId, cancellationToken)
            ?? throw new KeyNotFoundException("The requested user does not exist or is disabled.");

        var pairKey = PairKey(actorUserId, otherUserId);
        var candidateId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        using var transaction = connection.BeginTransaction();

        await using (var conversation = connection.CreateCommand())
        {
            conversation.Transaction = transaction;
            conversation.CommandText =
                """
                INSERT OR IGNORE INTO direct_conversations (
                    conversation_id, created_utc, pair_key
                ) VALUES ($id, $created, $pairKey);
                """;
            conversation.Parameters.AddWithValue("$id", candidateId.ToString("D"));
            conversation.Parameters.AddWithValue("$created", now.ToString("O"));
            conversation.Parameters.AddWithValue("$pairKey", pairKey);
            await conversation.ExecuteNonQueryAsync(cancellationToken);
        }

        Guid conversationId;
        DateTimeOffset createdUtc;
        await using (var resolve = connection.CreateCommand())
        {
            resolve.Transaction = transaction;
            resolve.CommandText =
                """
                SELECT conversation_id, created_utc
                FROM direct_conversations
                WHERE pair_key = $pairKey;
                """;
            resolve.Parameters.AddWithValue("$pairKey", pairKey);
            await using var reader = await resolve.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new InvalidOperationException("Direct conversation could not be resolved.");

            conversationId = Guid.Parse(reader.GetString(0));
            createdUtc = DateTimeOffset.Parse(reader.GetString(1));
        }

        foreach (var memberId in new[] { actorUserId, otherUserId })
        {
            await using var member = connection.CreateCommand();
            member.Transaction = transaction;
            member.CommandText =
                """
                INSERT OR IGNORE INTO direct_conversation_members (
                    conversation_id, user_id, joined_utc
                ) VALUES ($conversationId, $userId, $joined);
                """;
            member.Parameters.AddWithValue("$conversationId", conversationId.ToString("D"));
            member.Parameters.AddWithValue("$userId", memberId.ToString("D"));
            member.Parameters.AddWithValue("$joined", now.ToString("O"));
            await member.ExecuteNonQueryAsync(cancellationToken);
        }

        transaction.Commit();
        return new DirectConversationResponse(conversationId, other, createdUtc);
    }

    public async Task<IReadOnlyList<DirectConversationResponse>> ListDirectConversationsAsync(
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = database.OpenConnection();
        await RequireActiveUserAsync(connection, actorUserId, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT c.conversation_id, c.created_utc,
                   u.user_id, u.username, u.display_name, u.role
            FROM direct_conversations c
            JOIN direct_conversation_members mine
              ON mine.conversation_id = c.conversation_id
             AND mine.user_id = $actor
            JOIN direct_conversation_members other
              ON other.conversation_id = c.conversation_id
             AND other.user_id <> $actor
            JOIN users u
              ON u.user_id = other.user_id
             AND u.disabled_utc IS NULL
            ORDER BY c.created_utc DESC, c.conversation_id DESC;
            """;
        command.Parameters.AddWithValue("$actor", actorUserId.ToString("D"));

        var conversations = new List<DirectConversationResponse>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            conversations.Add(new DirectConversationResponse(
                Guid.Parse(reader.GetString(0)),
                new UserSummaryResponse(
                    Guid.Parse(reader.GetString(2)),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5)),
                DateTimeOffset.Parse(reader.GetString(1))));
        }

        return conversations;
    }

    public Task<IReadOnlyList<DirectConversationSummaryResponse>> ListDirectConversationSummariesAsync(
        Guid actorUserId,
        CancellationToken cancellationToken = default) =>
        ListDirectConversationSummariesAsync(
            actorUserId,
            includeArchived: true,
            cancellationToken);

    public async Task<IReadOnlyList<DirectConversationSummaryResponse>> ListDirectConversationSummariesAsync(
        Guid actorUserId,
        bool includeArchived,
        CancellationToken cancellationToken = default)
    {
        var conversations = await ListDirectConversationsAsync(actorUserId, cancellationToken);
        await using var connection = database.OpenConnection();

        var summaries = new List<DirectConversationSummaryResponse>(conversations.Count);

        foreach (var conversation in conversations)
        {
            MessageResponse? lastMessage = null;

            await using (var last = connection.CreateCommand())
            {
                last.CommandText =
                    """
                    SELECT message_id, scope_type, scope_id, sender_user_id,
                           client_message_id, body, reply_to_message_id, created_utc,
                           edited_utc, deleted_utc
                    FROM messages
                    WHERE scope_type = 'DIRECT'
                      AND scope_id = $conversationId
                    ORDER BY created_utc DESC, message_id DESC
                    LIMIT 1;
                    """;
                last.Parameters.AddWithValue(
                    "$conversationId",
                    conversation.ConversationId.ToString("D"));

                await using var reader = await last.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                    lastMessage = ReadMessage(reader);
            }

            int unreadCount;
            await using (var unread = connection.CreateCommand())
            {
                unread.CommandText =
                    """
                    SELECT COUNT(1)
                    FROM messages m
                    WHERE m.scope_type = 'DIRECT'
                      AND m.scope_id = $conversationId
                      AND m.deleted_utc IS NULL
                      AND m.sender_user_id <> $actor
                      AND NOT EXISTS (
                          SELECT 1
                          FROM message_receipts r
                          WHERE r.message_id = m.message_id
                            AND r.user_id = $actor
                            AND r.read_utc IS NOT NULL
                      );
                    """;
                unread.Parameters.AddWithValue(
                    "$conversationId",
                    conversation.ConversationId.ToString("D"));
                unread.Parameters.AddWithValue("$actor", actorUserId.ToString("D"));
                unreadCount = Convert.ToInt32(
                    await unread.ExecuteScalarAsync(cancellationToken));
            }

            bool isPinned = false;
            DateTimeOffset? mutedUntilUtc = null;
            bool isArchived = false;

            await using (var preference = connection.CreateCommand())
            {
                preference.CommandText =
                    """
                    SELECT pinned_utc, muted_until_utc, archived_utc
                    FROM direct_conversation_preferences
                    WHERE conversation_id = $conversationId
                      AND user_id = $actor;
                    """;
                preference.Parameters.AddWithValue(
                    "$conversationId",
                    conversation.ConversationId.ToString("D"));
                preference.Parameters.AddWithValue("$actor", actorUserId.ToString("D"));

                await using var preferenceReader =
                    await preference.ExecuteReaderAsync(cancellationToken);
                if (await preferenceReader.ReadAsync(cancellationToken))
                {
                    isPinned = !preferenceReader.IsDBNull(0);
                    mutedUntilUtc = preferenceReader.IsDBNull(1)
                        ? null
                        : DateTimeOffset.Parse(preferenceReader.GetString(1));
                    isArchived = !preferenceReader.IsDBNull(2);
                }
            }

            summaries.Add(new DirectConversationSummaryResponse(
                conversation.ConversationId,
                conversation.OtherUser,
                conversation.CreatedUtc,
                lastMessage,
                unreadCount,
                isPinned,
                mutedUntilUtc,
                isArchived));
        }

        return summaries
            .Where(summary => includeArchived || !summary.IsArchived)
            .OrderBy(summary => summary.IsArchived)
            .ThenByDescending(summary => summary.IsPinned)
            .ThenByDescending(summary =>
                summary.LastMessage?.CreatedUtc ?? summary.CreatedUtc)
            .ThenByDescending(summary => summary.ConversationId)
            .ToArray();
    }

    public async Task<IReadOnlyList<DirectConversationActivityResponse>> ListDirectConversationActivityAsync(
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = database.OpenConnection();
        await RequireActiveUserAsync(connection, actorUserId, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                c.conversation_id,
                COALESCE(MAX(m.created_utc), c.created_utc) AS activity_utc,
                (
                    SELECT m2.message_id
                    FROM messages m2
                    WHERE m2.scope_type = 'DIRECT'
                      AND m2.scope_id = c.conversation_id
                    ORDER BY m2.created_utc DESC, m2.message_id DESC
                    LIMIT 1
                ) AS last_message_id,
                (
                    SELECT COUNT(1)
                    FROM messages um
                    WHERE um.scope_type = 'DIRECT'
                      AND um.scope_id = c.conversation_id
                      AND um.deleted_utc IS NULL
                      AND um.sender_user_id <> $actor
                      AND NOT EXISTS (
                          SELECT 1
                          FROM message_receipts ur
                          WHERE ur.message_id = um.message_id
                            AND ur.user_id = $actor
                            AND ur.read_utc IS NOT NULL
                      )
                ) AS unread_count
            FROM direct_conversations c
            JOIN direct_conversation_members mine
              ON mine.conversation_id = c.conversation_id
             AND mine.user_id = $actor
            LEFT JOIN messages m
              ON m.scope_type = 'DIRECT'
             AND m.scope_id = c.conversation_id
            GROUP BY c.conversation_id, c.created_utc
            ORDER BY activity_utc DESC, c.conversation_id DESC;
            """;
        command.Parameters.AddWithValue("$actor", actorUserId.ToString("D"));

        var rows = new List<DirectConversationActivityResponse>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new DirectConversationActivityResponse(
                Guid.Parse(reader.GetString(0)),
                DateTimeOffset.Parse(reader.GetString(1)),
                reader.IsDBNull(2) ? null : Guid.Parse(reader.GetString(2)),
                checked((int)reader.GetInt64(3))));
        }

        return rows;
    }

    public async Task<DirectConversationPreferenceResponse> GetDirectConversationPreferenceAsync(
        Guid actorUserId,
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = database.OpenConnection();
        await RequireDirectMembershipAsync(
            connection,
            actorUserId,
            conversationId,
            cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT pinned_utc, muted_until_utc, archived_utc, updated_utc
            FROM direct_conversation_preferences
            WHERE conversation_id = $conversationId
              AND user_id = $userId;
            """;
        command.Parameters.AddWithValue("$conversationId", conversationId.ToString("D"));
        command.Parameters.AddWithValue("$userId", actorUserId.ToString("D"));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new DirectConversationPreferenceResponse(
                conversationId,
                IsPinned: false,
                MutedUntilUtc: null,
                IsArchived: false,
                UpdatedUtc: DateTimeOffset.MinValue);
        }

        return new DirectConversationPreferenceResponse(
            conversationId,
            !reader.IsDBNull(0),
            reader.IsDBNull(1) ? null : DateTimeOffset.Parse(reader.GetString(1)),
            !reader.IsDBNull(2),
            DateTimeOffset.Parse(reader.GetString(3)));
    }

    public async Task<DirectConversationPreferenceResponse> UpdateDirectConversationPreferenceAsync(
        Guid actorUserId,
        Guid conversationId,
        UpdateDirectConversationPreferenceRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var connection = database.OpenConnection();
        await RequireDirectMembershipAsync(
            connection,
            actorUserId,
            conversationId,
            cancellationToken);

        var now = DateTimeOffset.UtcNow;
        DateTimeOffset? mutedUntil = request.MutedUntilUtc is { } requestedMute && requestedMute > now
            ? requestedMute
            : null;

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO direct_conversation_preferences (
                conversation_id, user_id, pinned_utc,
                muted_until_utc, archived_utc, updated_utc
            ) VALUES (
                $conversationId, $userId, $pinnedUtc,
                $mutedUntilUtc, $archivedUtc, $updatedUtc
            )
            ON CONFLICT(conversation_id, user_id) DO UPDATE SET
                pinned_utc = excluded.pinned_utc,
                muted_until_utc = excluded.muted_until_utc,
                archived_utc = excluded.archived_utc,
                updated_utc = excluded.updated_utc;
            """;
        command.Parameters.AddWithValue("$conversationId", conversationId.ToString("D"));
        command.Parameters.AddWithValue("$userId", actorUserId.ToString("D"));
        command.Parameters.AddWithValue("$pinnedUtc", request.IsPinned ? now.ToString("O") : DBNull.Value);
        command.Parameters.AddWithValue("$mutedUntilUtc", mutedUntil is null ? DBNull.Value : mutedUntil.Value.ToString("O"));
        command.Parameters.AddWithValue("$archivedUtc", request.IsArchived ? now.ToString("O") : DBNull.Value);
        command.Parameters.AddWithValue("$updatedUtc", now.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);

        return new DirectConversationPreferenceResponse(
            conversationId,
            request.IsPinned,
            mutedUntil,
            request.IsArchived,
            now);
    }

    public async Task<DirectUnreadSummaryResponse> GetDirectUnreadSummaryAsync(
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = database.OpenConnection();
        await RequireActiveUserAsync(connection, actorUserId, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                COUNT(1) AS unread_messages,
                COUNT(DISTINCT m.scope_id) AS unread_conversations
            FROM messages m
            JOIN direct_conversation_members mine
              ON mine.conversation_id = m.scope_id
             AND mine.user_id = $actor
            WHERE m.scope_type = 'DIRECT'
              AND m.deleted_utc IS NULL
              AND m.sender_user_id <> $actor
              AND NOT EXISTS (
                  SELECT 1
                  FROM message_receipts r
                  WHERE r.message_id = m.message_id
                    AND r.user_id = $actor
                    AND r.read_utc IS NOT NULL
              );
            """;
        command.Parameters.AddWithValue("$actor", actorUserId.ToString("D"));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return new DirectUnreadSummaryResponse(0, 0);

        return new DirectUnreadSummaryResponse(
            checked((int)reader.GetInt64(0)),
            checked((int)reader.GetInt64(1)));
    }

    public async Task<PersistedMessageResult> SendDirectMessageAsync(
        Guid actorUserId,
        Guid conversationId,
        SendMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.ClientMessageId == Guid.Empty)
            throw new ArgumentException("ClientMessageId must be non-empty.");

        var body = MessageTextPolicy.Normalize(
            request.Body,
            MaxMessageLength);
        await using var connection = database.OpenConnection();
        await RequireDirectMembershipAsync(connection, actorUserId, conversationId, cancellationToken);

        using var transaction = connection.BeginTransaction();

        var existing = await FindByClientMessageIdAsync(
            connection,
            transaction,
            actorUserId,
            request.ClientMessageId,
            cancellationToken);

        if (existing is not null)
        {
            EnsureIdempotentReplayMatches(existing, conversationId, body, request.ReplyToMessageId);

            transaction.Commit();
            return new PersistedMessageResult(existing, false);
        }

        if (request.ReplyToMessageId is { } replyTo)
        {
            await using var reply = connection.CreateCommand();
            reply.Transaction = transaction;
            reply.CommandText =
                """
                SELECT COUNT(1)
                FROM messages
                WHERE message_id = $messageId
                  AND scope_type = 'DIRECT'
                  AND scope_id = $conversationId
                  AND deleted_utc IS NULL;
                """;
            reply.Parameters.AddWithValue("$messageId", replyTo.ToString("D"));
            reply.Parameters.AddWithValue("$conversationId", conversationId.ToString("D"));
            if (Convert.ToInt64(await reply.ExecuteScalarAsync(cancellationToken)) != 1)
                throw new ArgumentException("Reply target is not an active message in this conversation.");
        }

        var messageId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var inserted = 0;
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText =
                """
                INSERT OR IGNORE INTO messages (
                    message_id, scope_type, scope_id, sender_user_id,
                    client_message_id, body, reply_to_message_id, created_utc,
                           edited_utc, deleted_utc
                ) VALUES (
                    $messageId, 'DIRECT', $scopeId, $sender,
                    $clientMessageId, $body, $replyTo, $created,
                    NULL, NULL
                );
                """;
            insert.Parameters.AddWithValue("$messageId", messageId.ToString("D"));
            insert.Parameters.AddWithValue("$scopeId", conversationId.ToString("D"));
            insert.Parameters.AddWithValue("$sender", actorUserId.ToString("D"));
            insert.Parameters.AddWithValue("$clientMessageId", request.ClientMessageId.ToString("D"));
            insert.Parameters.AddWithValue("$body", body);
            insert.Parameters.AddWithValue("$replyTo", request.ReplyToMessageId is null
                ? DBNull.Value
                : request.ReplyToMessageId.Value.ToString("D"));
            insert.Parameters.AddWithValue("$created", now.ToString("O"));
            inserted = await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        if (inserted == 0)
        {
            var concurrentExisting = await FindByClientMessageIdAsync(
                connection,
                transaction,
                actorUserId,
                request.ClientMessageId,
                cancellationToken)
                ?? throw new InvalidOperationException(
                    "Message idempotency conflict could not be resolved.");

            EnsureIdempotentReplayMatches(
                concurrentExisting,
                conversationId,
                body,
                request.ReplyToMessageId);

            transaction.Commit();
            return new PersistedMessageResult(concurrentExisting, false);
        }

        transaction.Commit();

        return new PersistedMessageResult(
            new MessageResponse(
                messageId,
                "DIRECT",
                conversationId,
                actorUserId,
                request.ClientMessageId,
                body,
                request.ReplyToMessageId,
                now),
            true);
    }

    public async Task<DirectGlobalMessageSearchResponse> SearchAllDirectMessagesAsync(
        Guid actorUserId,
        string query,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = (query ?? string.Empty)
            .Trim()
            .Normalize(System.Text.NormalizationForm.FormC);

        if (normalizedQuery.Length is < 1 or > 128)
            throw new ArgumentException(
                "Search query must contain between 1 and 128 characters.",
                nameof(query));

        if (limit is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(limit));

        await using var connection = database.OpenConnection();
        await RequireActiveUserAsync(connection, actorUserId, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                m.scope_id,
                peer.user_id,
                peer.username,
                peer.display_name,
                peer.role,
                m.message_id,
                m.scope_type,
                m.scope_id,
                m.sender_user_id,
                m.client_message_id,
                m.body,
                m.reply_to_message_id,
                m.created_utc,
                m.edited_utc,
                m.deleted_utc
            FROM messages m
            JOIN direct_conversation_members mine
              ON mine.conversation_id = m.scope_id
             AND mine.user_id = $actor
            JOIN direct_conversation_members other
              ON other.conversation_id = m.scope_id
             AND other.user_id <> $actor
            JOIN users peer
              ON peer.user_id = other.user_id
             AND peer.disabled_utc IS NULL
            WHERE m.scope_type = 'DIRECT'
              AND m.deleted_utc IS NULL
              AND m.body IS NOT NULL
              AND instr(lower(m.body), lower($query)) > 0
            ORDER BY m.created_utc DESC, m.message_id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$actor", actorUserId.ToString("D"));
        command.Parameters.AddWithValue("$query", normalizedQuery);
        command.Parameters.AddWithValue("$limit", limit);

        var hits = new List<DirectGlobalMessageSearchHitResponse>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            hits.Add(new DirectGlobalMessageSearchHitResponse(
                Guid.Parse(reader.GetString(0)),
                new UserSummaryResponse(
                    Guid.Parse(reader.GetString(1)),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4)),
                new MessageResponse(
                    Guid.Parse(reader.GetString(5)),
                    reader.GetString(6),
                    Guid.Parse(reader.GetString(7)),
                    Guid.Parse(reader.GetString(8)),
                    Guid.Parse(reader.GetString(9)),
                    reader.IsDBNull(10) ? string.Empty : reader.GetString(10),
                    reader.IsDBNull(11) ? null : Guid.Parse(reader.GetString(11)),
                    DateTimeOffset.Parse(reader.GetString(12)),
                    reader.IsDBNull(13) ? null : DateTimeOffset.Parse(reader.GetString(13)),
                    reader.IsDBNull(14) ? null : DateTimeOffset.Parse(reader.GetString(14)))));
        }

        return new DirectGlobalMessageSearchResponse(
            normalizedQuery,
            hits);
    }

    public async Task<DirectMessageSearchResponse> SearchDirectMessagesAsync(
        Guid actorUserId,
        Guid conversationId,
        string query,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = (query ?? string.Empty)
            .Trim()
            .Normalize(System.Text.NormalizationForm.FormC);

        if (normalizedQuery.Length is < 1 or > 128)
            throw new ArgumentException(
                "Search query must contain between 1 and 128 characters.",
                nameof(query));

        if (limit is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(limit));

        await using var connection = database.OpenConnection();
        await RequireDirectMembershipAsync(
            connection,
            actorUserId,
            conversationId,
            cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT message_id, scope_type, scope_id, sender_user_id,
                   client_message_id, body, reply_to_message_id, created_utc,
                   edited_utc, deleted_utc
            FROM messages
            WHERE scope_type = 'DIRECT'
              AND scope_id = $conversationId
              AND deleted_utc IS NULL
              AND body IS NOT NULL
              AND instr(lower(body), lower($query)) > 0
            ORDER BY created_utc DESC, message_id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue(
            "$conversationId",
            conversationId.ToString("D"));
        command.Parameters.AddWithValue("$query", normalizedQuery);
        command.Parameters.AddWithValue("$limit", limit);

        var items = new List<MessageResponse>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            items.Add(ReadMessage(reader));

        return new DirectMessageSearchResponse(
            conversationId,
            normalizedQuery,
            items);
    }

    public async Task<MessageResponse> GetDirectMessageByIdAsync(
        Guid actorUserId,
        Guid conversationId,
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = database.OpenConnection();
        await RequireDirectMembershipAsync(
            connection,
            actorUserId,
            conversationId,
            cancellationToken);

        return await GetDirectMessageAsync(
            connection,
            transaction: null,
            conversationId,
            messageId,
            cancellationToken)
            ?? throw new KeyNotFoundException("Message not found.");
    }

    public async Task<MessageResponse> EditDirectMessageAsync(
        Guid actorUserId,
        Guid conversationId,
        Guid messageId,
        EditMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        var body = MessageTextPolicy.Normalize(
            request.Body,
            MaxMessageLength);

        await using var connection = database.OpenConnection();
        await RequireDirectMembershipAsync(
            connection,
            actorUserId,
            conversationId,
            cancellationToken);

        using var transaction = connection.BeginTransaction();

        var existing = await GetDirectMessageAsync(
            connection,
            transaction,
            conversationId,
            messageId,
            cancellationToken)
            ?? throw new KeyNotFoundException("Message not found.");

        if (existing.DeletedUtc is not null)
            throw new InvalidOperationException("Deleted messages cannot be edited.");

        if (existing.SenderUserId != actorUserId)
            throw new UnauthorizedAccessException("Only the sender can edit this message.");

        var editedUtc = DateTimeOffset.UtcNow;

        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText =
                """
                UPDATE messages
                SET body = $body,
                    edited_utc = $editedUtc
                WHERE message_id = $messageId
                  AND deleted_utc IS NULL;
                """;
            update.Parameters.AddWithValue("$body", body);
            update.Parameters.AddWithValue("$editedUtc", editedUtc.ToString("O"));
            update.Parameters.AddWithValue("$messageId", messageId.ToString("D"));

            if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidOperationException("Message edit could not be persisted.");
        }

        var edited = await GetDirectMessageAsync(
            connection,
            transaction,
            conversationId,
            messageId,
            cancellationToken)
            ?? throw new InvalidOperationException("Edited message could not be reloaded.");

        transaction.Commit();
        return edited;
    }

    public async Task<MessageDeletedResponse> DeleteDirectMessageAsync(
        Guid actorUserId,
        Guid conversationId,
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = database.OpenConnection();
        await RequireDirectMembershipAsync(
            connection,
            actorUserId,
            conversationId,
            cancellationToken);

        using var transaction = connection.BeginTransaction();

        var existing = await GetDirectMessageAsync(
            connection,
            transaction,
            conversationId,
            messageId,
            cancellationToken)
            ?? throw new KeyNotFoundException("Message not found.");

        if (existing.SenderUserId != actorUserId)
            throw new UnauthorizedAccessException("Only the sender can delete this message.");

        if (existing.DeletedUtc is { } alreadyDeleted)
        {
            transaction.Commit();
            return new MessageDeletedResponse(
                messageId,
                conversationId,
                alreadyDeleted);
        }

        var deletedUtc = DateTimeOffset.UtcNow;

        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText =
                """
                UPDATE messages
                SET body = NULL,
                    deleted_utc = $deletedUtc
                WHERE message_id = $messageId
                  AND deleted_utc IS NULL;
                """;
            update.Parameters.AddWithValue("$deletedUtc", deletedUtc.ToString("O"));
            update.Parameters.AddWithValue("$messageId", messageId.ToString("D"));

            if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidOperationException("Message delete could not be persisted.");
        }

        transaction.Commit();
        return new MessageDeletedResponse(
            messageId,
            conversationId,
            deletedUtc);
    }

    public async Task<IReadOnlyList<MessageResponse>> GetDirectHistoryAsync(
        Guid actorUserId,
        Guid conversationId,
        Guid? afterMessageId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 250)
            throw new ArgumentOutOfRangeException(nameof(limit));

        await using var connection = database.OpenConnection();
        await RequireDirectMembershipAsync(connection, actorUserId, conversationId, cancellationToken);

        string? afterCreated = null;
        string? afterId = null;
        if (afterMessageId is { } cursor)
        {
            await using var cursorCommand = connection.CreateCommand();
            cursorCommand.CommandText =
                """
                SELECT created_utc, message_id
                FROM messages
                WHERE message_id = $messageId
                  AND scope_type = 'DIRECT'
                  AND scope_id = $conversationId;
                """;
            cursorCommand.Parameters.AddWithValue("$messageId", cursor.ToString("D"));
            cursorCommand.Parameters.AddWithValue("$conversationId", conversationId.ToString("D"));
            await using var reader = await cursorCommand.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new KeyNotFoundException("Message cursor was not found in this conversation.");

            afterCreated = reader.GetString(0);
            afterId = reader.GetString(1);
        }

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT message_id, scope_type, scope_id, sender_user_id,
                   client_message_id, body, reply_to_message_id, created_utc,
                           edited_utc, deleted_utc
            FROM messages
            WHERE scope_type = 'DIRECT'
              AND scope_id = $conversationId
              AND (
                  $afterCreated IS NULL
                  OR created_utc > $afterCreated
                  OR (created_utc = $afterCreated AND message_id > $afterId)
              )
            ORDER BY created_utc, message_id
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$conversationId", conversationId.ToString("D"));
        command.Parameters.AddWithValue("$afterCreated", afterCreated is null ? DBNull.Value : afterCreated);
        command.Parameters.AddWithValue("$afterId", afterId is null ? DBNull.Value : afterId);
        command.Parameters.AddWithValue("$limit", limit);

        var messages = new List<MessageResponse>();
        await using var history = await command.ExecuteReaderAsync(cancellationToken);
        while (await history.ReadAsync(cancellationToken))
        {
            messages.Add(ReadMessage(history));
        }

        return messages;
    }

    public async Task<RecentMessagePageResponse> GetDirectRecentHistoryPageAsync(
        Guid actorUserId,
        Guid conversationId,
        Guid? beforeMessageId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > MaxMessagePageSize)
            throw new ArgumentOutOfRangeException(nameof(limit));

        await using var connection = database.OpenConnection();
        await RequireDirectMembershipAsync(
            connection,
            actorUserId,
            conversationId,
            cancellationToken);

        string? beforeCreated = null;
        string? beforeId = null;

        if (beforeMessageId is { } cursor)
        {
            await using var cursorCommand = connection.CreateCommand();
            cursorCommand.CommandText =
                """
                SELECT created_utc, message_id
                FROM messages
                WHERE message_id = $messageId
                  AND scope_type = 'DIRECT'
                  AND scope_id = $conversationId;
                """;
            cursorCommand.Parameters.AddWithValue("$messageId", cursor.ToString("D"));
            cursorCommand.Parameters.AddWithValue("$conversationId", conversationId.ToString("D"));

            await using var cursorReader =
                await cursorCommand.ExecuteReaderAsync(cancellationToken);

            if (!await cursorReader.ReadAsync(cancellationToken))
                throw new KeyNotFoundException(
                    "Message cursor was not found in this conversation.");

            beforeCreated = cursorReader.GetString(0);
            beforeId = cursorReader.GetString(1);
        }

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT message_id, scope_type, scope_id, sender_user_id,
                   client_message_id, body, reply_to_message_id, created_utc,
                   edited_utc, deleted_utc
            FROM messages
            WHERE scope_type = 'DIRECT'
              AND scope_id = $conversationId
              AND (
                  $beforeCreated IS NULL
                  OR created_utc < $beforeCreated
                  OR (created_utc = $beforeCreated AND message_id < $beforeId)
              )
            ORDER BY created_utc DESC, message_id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$conversationId", conversationId.ToString("D"));
        command.Parameters.AddWithValue(
            "$beforeCreated",
            beforeCreated is null ? DBNull.Value : beforeCreated);
        command.Parameters.AddWithValue(
            "$beforeId",
            beforeId is null ? DBNull.Value : beforeId);
        command.Parameters.AddWithValue("$limit", limit + 1);

        var descending = new List<MessageResponse>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            descending.Add(ReadMessage(reader));

        var hasOlder = descending.Count > limit;
        if (hasOlder)
            descending.RemoveAt(descending.Count - 1);

        descending.Reverse();

        return new RecentMessagePageResponse(
            descending,
            descending.Count == 0 ? null : descending[0].MessageId,
            hasOlder);
    }

    public async Task<MessagePageResponse> GetDirectHistoryPageAsync(
        Guid actorUserId,
        Guid conversationId,
        Guid? afterMessageId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > MaxMessagePageSize)
            throw new ArgumentOutOfRangeException(nameof(limit));

        var items = await GetDirectHistoryAsync(
            actorUserId,
            conversationId,
            afterMessageId,
            limit + 1,
            cancellationToken);

        var hasMore = items.Count > limit;
        var pageItems = hasMore
            ? items.Take(limit).ToArray()
            : items.ToArray();

        return new MessagePageResponse(
            pageItems,
            pageItems.Length == 0 ? null : pageItems[^1].MessageId,
            hasMore);
    }

    public async Task<IReadOnlyList<Guid>> GetDirectMemberIdsAsync(
        Guid actorUserId,
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = database.OpenConnection();
        await RequireDirectMembershipAsync(connection, actorUserId, conversationId, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT user_id
            FROM direct_conversation_members
            WHERE conversation_id = $conversationId
            ORDER BY user_id;
            """;
        command.Parameters.AddWithValue("$conversationId", conversationId.ToString("D"));

        var members = new List<Guid>(2);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            members.Add(Guid.Parse(reader.GetString(0)));

        return members;
    }

    public async Task<ConversationReadResponse> MarkConversationReadAsync(
        Guid actorUserId,
        Guid conversationId,
        Guid? upToMessageId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = database.OpenConnection();
        await RequireDirectMembershipAsync(
            connection,
            actorUserId,
            conversationId,
            cancellationToken);

        string? cutoffCreated = null;
        string? cutoffId = null;

        if (upToMessageId is { } cursor)
        {
            await using var cutoff = connection.CreateCommand();
            cutoff.CommandText =
                """
                SELECT created_utc, message_id
                FROM messages
                WHERE message_id = $messageId
                  AND scope_type = 'DIRECT'
                  AND scope_id = $conversationId;
                """;
            cutoff.Parameters.AddWithValue("$messageId", cursor.ToString("D"));
            cutoff.Parameters.AddWithValue("$conversationId", conversationId.ToString("D"));

            await using var reader = await cutoff.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new KeyNotFoundException("Read cursor was not found in this conversation.");

            cutoffCreated = reader.GetString(0);
            cutoffId = reader.GetString(1);
        }

        var messageIds = new List<Guid>();
        await using (var candidates = connection.CreateCommand())
        {
            candidates.CommandText =
                """
                SELECT m.message_id
                FROM messages m
                WHERE m.scope_type = 'DIRECT'
                  AND m.scope_id = $conversationId
                  AND m.deleted_utc IS NULL
                  AND m.sender_user_id <> $actor
                  AND (
                      $cutoffCreated IS NULL
                      OR m.created_utc < $cutoffCreated
                      OR (m.created_utc = $cutoffCreated AND m.message_id <= $cutoffId)
                  )
                  AND NOT EXISTS (
                      SELECT 1
                      FROM message_receipts r
                      WHERE r.message_id = m.message_id
                        AND r.user_id = $actor
                        AND r.read_utc IS NOT NULL
                  )
                ORDER BY m.created_utc, m.message_id;
                """;
            candidates.Parameters.AddWithValue("$conversationId", conversationId.ToString("D"));
            candidates.Parameters.AddWithValue("$actor", actorUserId.ToString("D"));
            candidates.Parameters.AddWithValue(
                "$cutoffCreated",
                cutoffCreated is null ? DBNull.Value : cutoffCreated);
            candidates.Parameters.AddWithValue(
                "$cutoffId",
                cutoffId is null ? DBNull.Value : cutoffId);

            await using var reader = await candidates.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                messageIds.Add(Guid.Parse(reader.GetString(0)));
        }

        var readUtc = DateTimeOffset.UtcNow;
        using var transaction = connection.BeginTransaction();

        foreach (var messageId in messageIds)
        {
            await using var receipt = connection.CreateCommand();
            receipt.Transaction = transaction;
            receipt.CommandText =
                """
                INSERT INTO message_receipts (
                    message_id, user_id, delivered_utc, read_utc
                ) VALUES (
                    $messageId, $userId, $utc, $utc
                )
                ON CONFLICT(message_id, user_id) DO UPDATE SET
                    delivered_utc = COALESCE(message_receipts.delivered_utc, excluded.delivered_utc),
                    read_utc = COALESCE(message_receipts.read_utc, excluded.read_utc);
                """;
            receipt.Parameters.AddWithValue("$messageId", messageId.ToString("D"));
            receipt.Parameters.AddWithValue("$userId", actorUserId.ToString("D"));
            receipt.Parameters.AddWithValue("$utc", readUtc.ToString("O"));
            await receipt.ExecuteNonQueryAsync(cancellationToken);
        }

        transaction.Commit();

        return new ConversationReadResponse(
            conversationId,
            messageIds.Count,
            readUtc);
    }

    public async Task<Guid> GetDirectConversationIdForMessageAsync(
        Guid actorUserId,
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = database.OpenConnection();
        var target = await GetDirectReceiptTargetAsync(
            connection,
            messageId,
            cancellationToken);

        await RequireDirectMembershipAsync(
            connection,
            actorUserId,
            target.ConversationId,
            cancellationToken);

        return target.ConversationId;
    }

    public async Task MarkDeliveredAsync(
        Guid actorUserId,
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = database.OpenConnection();
        var target = await GetDirectReceiptTargetAsync(connection, messageId, cancellationToken);

        await RequireDirectMembershipAsync(connection, actorUserId, target.ConversationId, cancellationToken);
        if (target.SenderUserId == actorUserId)
            throw new InvalidOperationException("A sender cannot acknowledge their own message as delivered.");

        var now = DateTimeOffset.UtcNow.ToString("O");

        await using var receipt = connection.CreateCommand();
        receipt.CommandText =
            """
            INSERT INTO message_receipts (
                message_id, user_id, delivered_utc, read_utc
            ) VALUES ($messageId, $userId, $utc, NULL)
            ON CONFLICT(message_id, user_id) DO UPDATE SET
                delivered_utc = COALESCE(message_receipts.delivered_utc, excluded.delivered_utc);
            """;
        receipt.Parameters.AddWithValue("$messageId", messageId.ToString("D"));
        receipt.Parameters.AddWithValue("$userId", actorUserId.ToString("D"));
        receipt.Parameters.AddWithValue("$utc", now);
        await receipt.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task MarkReadAsync(
        Guid actorUserId,
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = database.OpenConnection();
        var target = await GetDirectReceiptTargetAsync(connection, messageId, cancellationToken);

        await RequireDirectMembershipAsync(connection, actorUserId, target.ConversationId, cancellationToken);
        if (target.SenderUserId == actorUserId)
            throw new InvalidOperationException("A sender cannot acknowledge their own message as read.");

        var now = DateTimeOffset.UtcNow.ToString("O");

        await using var receipt = connection.CreateCommand();
        receipt.CommandText =
            """
            INSERT INTO message_receipts (
                message_id, user_id, delivered_utc, read_utc
            ) VALUES ($messageId, $userId, $utc, $utc)
            ON CONFLICT(message_id, user_id) DO UPDATE SET
                delivered_utc = COALESCE(message_receipts.delivered_utc, excluded.delivered_utc),
                read_utc = COALESCE(message_receipts.read_utc, excluded.read_utc);
            """;
        receipt.Parameters.AddWithValue("$messageId", messageId.ToString("D"));
        receipt.Parameters.AddWithValue("$userId", actorUserId.ToString("D"));
        receipt.Parameters.AddWithValue("$utc", now);
        await receipt.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MessageReceiptResponse>> GetReceiptsAsync(
        Guid actorUserId,
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = database.OpenConnection();
        var target = await GetDirectReceiptTargetAsync(connection, messageId, cancellationToken);
        await RequireDirectMembershipAsync(connection, actorUserId, target.ConversationId, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT message_id, user_id, delivered_utc, read_utc
            FROM message_receipts
            WHERE message_id = $messageId
            ORDER BY user_id;
            """;
        command.Parameters.AddWithValue("$messageId", messageId.ToString("D"));

        var receipts = new List<MessageReceiptResponse>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            receipts.Add(new MessageReceiptResponse(
                Guid.Parse(reader.GetString(0)),
                Guid.Parse(reader.GetString(1)),
                reader.IsDBNull(2) ? null : DateTimeOffset.Parse(reader.GetString(2)),
                reader.IsDBNull(3) ? null : DateTimeOffset.Parse(reader.GetString(3))));
        }

        return receipts;
    }

    private static async Task<MessageResponse?> GetDirectMessageAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        Guid conversationId,
        Guid messageId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT message_id, scope_type, scope_id, sender_user_id,
                   client_message_id, body, reply_to_message_id, created_utc,
                   edited_utc, deleted_utc
            FROM messages
            WHERE message_id = $messageId
              AND scope_type = 'DIRECT'
              AND scope_id = $conversationId;
            """;
        command.Parameters.AddWithValue("$messageId", messageId.ToString("D"));
        command.Parameters.AddWithValue("$conversationId", conversationId.ToString("D"));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadMessage(reader)
            : null;
    }

    private static async Task<(Guid ConversationId, Guid SenderUserId)> GetDirectReceiptTargetAsync(
        SqliteConnection connection,
        Guid messageId,
        CancellationToken cancellationToken)
    {
        await using var message = connection.CreateCommand();
        message.CommandText =
            """
            SELECT scope_id, sender_user_id
            FROM messages
            WHERE message_id = $messageId
              AND scope_type = 'DIRECT'
              AND deleted_utc IS NULL;
            """;
        message.Parameters.AddWithValue("$messageId", messageId.ToString("D"));

        await using var reader = await message.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new KeyNotFoundException("Message not found.");

        return (
            Guid.Parse(reader.GetString(0)),
            Guid.Parse(reader.GetString(1)));
    }

    private static void EnsureIdempotentReplayMatches(
        MessageResponse existing,
        Guid conversationId,
        string body,
        Guid? replyToMessageId)
    {
        if (existing.ScopeType != "DIRECT" ||
            existing.ScopeId != conversationId ||
            !string.Equals(existing.Body, body, StringComparison.Ordinal) ||
            existing.ReplyToMessageId != replyToMessageId)
        {
            throw new InvalidOperationException(
                "ClientMessageId was already used with different message content.");
        }
    }

    private static string PairKey(Guid left, Guid right)
    {
        var a = left.ToString("D");
        var b = right.ToString("D");
        return string.CompareOrdinal(a, b) < 0 ? $"{a}:{b}" : $"{b}:{a}";
    }

    private static async Task RequireActiveUserAsync(
        SqliteConnection connection,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (await GetActiveUserAsync(connection, userId, cancellationToken) is null)
            throw new UnauthorizedAccessException("Active user is required.");
    }

    private static async Task<UserSummaryResponse?> GetActiveUserAsync(
        SqliteConnection connection,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT user_id, username, display_name, role
            FROM users
            WHERE user_id = $userId
              AND disabled_utc IS NULL;
            """;
        command.Parameters.AddWithValue("$userId", userId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new UserSummaryResponse(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3));
    }

    private static async Task RequireDirectMembershipAsync(
        SqliteConnection connection,
        Guid userId,
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(1)
            FROM direct_conversation_members m
            JOIN users u ON u.user_id = m.user_id
            WHERE m.conversation_id = $conversationId
              AND m.user_id = $userId
              AND u.disabled_utc IS NULL;
            """;
        command.Parameters.AddWithValue("$conversationId", conversationId.ToString("D"));
        command.Parameters.AddWithValue("$userId", userId.ToString("D"));
        if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) != 1)
            throw new UnauthorizedAccessException("Direct conversation membership is required.");
    }

    private static async Task<MessageResponse?> FindByClientMessageIdAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid senderUserId,
        Guid clientMessageId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT message_id, scope_type, scope_id, sender_user_id,
                   client_message_id, body, reply_to_message_id, created_utc,
                           edited_utc, deleted_utc
            FROM messages
            WHERE sender_user_id = $sender
              AND client_message_id = $clientMessageId;
            """;
        command.Parameters.AddWithValue("$sender", senderUserId.ToString("D"));
        command.Parameters.AddWithValue("$clientMessageId", clientMessageId.ToString("D"));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return ReadMessage(reader);
    }

    private static MessageResponse ReadMessage(SqliteDataReader reader) =>
        new(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            Guid.Parse(reader.GetString(2)),
            Guid.Parse(reader.GetString(3)),
            Guid.Parse(reader.GetString(4)),
            reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
            reader.IsDBNull(6) ? null : Guid.Parse(reader.GetString(6)),
            DateTimeOffset.Parse(reader.GetString(7)),
            reader.IsDBNull(8) ? null : DateTimeOffset.Parse(reader.GetString(8)),
            reader.IsDBNull(9) ? null : DateTimeOffset.Parse(reader.GetString(9)));
}
