using System.Collections.Concurrent;
using InterLan.Contracts;
using InterLan.Domain;
using Microsoft.Data.Sqlite;

namespace InterLan.Infrastructure;

/// <summary>
/// Uses the already-migrated scope-typed messages table. Every send and replay
/// checks active membership under the same SQLite write transaction.
/// </summary>
public sealed class GroupMessageStore(SqliteDatabase database)
{
    private const int MaxBodyLength = 4_000;
    public const int MaxPageSize = 200;

    public async Task<PersistedMessageResult> SendAsync(
        Guid actorUserId, Guid groupId, SendMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.ClientMessageId == Guid.Empty)
            throw new ArgumentException("ClientMessageId must be non-empty.");
        var body = MessageTextPolicy.Normalize(request.Body, MaxBodyLength);

        await using var connection = database.OpenConnection();
        // A reserved SQLite writer serializes membership mutations and sends.
        using var transaction = connection.BeginTransaction(deferred: false);
        await RequireActiveMemberAsync(connection, transaction, actorUserId, groupId, cancellationToken);

        await using (var find = connection.CreateCommand())
        {
            find.Transaction = transaction;
            find.CommandText =
                """
                SELECT message_id, scope_type, scope_id, sender_user_id,
                       client_message_id, body, reply_to_message_id,
                       created_utc, edited_utc, deleted_utc
                FROM messages
                WHERE sender_user_id = $sender AND client_message_id = $clientId;
                """;
            find.Parameters.AddWithValue("$sender", actorUserId.ToString("D"));
            find.Parameters.AddWithValue("$clientId", request.ClientMessageId.ToString("D"));
            await using var existingReader = await find.ExecuteReaderAsync(cancellationToken);
            if (await existingReader.ReadAsync(cancellationToken))
            {
                var existing = ReadMessage(existingReader);
                if (existing.ScopeType != "GROUP" || existing.ScopeId != groupId ||
                    existing.Body != body || existing.ReplyToMessageId != request.ReplyToMessageId)
                    throw new InvalidOperationException("ClientMessageId has already been used for different content.");
                await existingReader.DisposeAsync();
                transaction.Commit();
                return new PersistedMessageResult(existing, false);
            }
        }

        if (request.ReplyToMessageId is { } replyId)
        {
            await using var checkReply = connection.CreateCommand();
            checkReply.Transaction = transaction;
            checkReply.CommandText =
                """
                SELECT COUNT(1) FROM messages
                WHERE message_id = $id AND scope_type = 'GROUP'
                  AND scope_id = $group AND deleted_utc IS NULL;
                """;
            checkReply.Parameters.AddWithValue("$id", replyId.ToString("D"));
            checkReply.Parameters.AddWithValue("$group", groupId.ToString("D"));
            if (Convert.ToInt64(await checkReply.ExecuteScalarAsync(cancellationToken)) != 1)
                throw new ArgumentException("Reply target is not a live message in this group.");
        }

        var id = Guid.NewGuid();
        var createdUtc = DateTimeOffset.UtcNow;
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText =
                """
                INSERT INTO messages (
                    message_id, scope_type, scope_id, sender_user_id,
                    client_message_id, body, reply_to_message_id,
                    created_utc, edited_utc, deleted_utc
                ) VALUES (
                    $id, 'GROUP', $group, $sender, $client,
                    $body, $reply, $created, NULL, NULL
                );
                """;
            insert.Parameters.AddWithValue("$id", id.ToString("D"));
            insert.Parameters.AddWithValue("$group", groupId.ToString("D"));
            insert.Parameters.AddWithValue("$sender", actorUserId.ToString("D"));
            insert.Parameters.AddWithValue("$client", request.ClientMessageId.ToString("D"));
            insert.Parameters.AddWithValue("$body", body);
            insert.Parameters.AddWithValue("$reply", request.ReplyToMessageId is { } reply
                ? reply.ToString("D") : DBNull.Value);
            insert.Parameters.AddWithValue("$created", createdUtc.ToString("O"));
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        transaction.Commit();
        return new PersistedMessageResult(
            new MessageResponse(id, "GROUP", groupId, actorUserId,
                request.ClientMessageId, body, request.ReplyToMessageId, createdUtc),
            true);
    }

    public async Task<MessagePageResponse> GetPageAsync(
        Guid actorUserId, Guid groupId, Guid? afterMessageId, int limit = 50,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > MaxPageSize)
            throw new ArgumentOutOfRangeException(nameof(limit));

        await using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: true);
        await RequireActiveMemberAsync(connection, transaction, actorUserId, groupId, cancellationToken);

        string? afterCreated = null;
        string? afterId = null;
        if (afterMessageId is { } cursor)
        {
            await using var resolve = connection.CreateCommand();
            resolve.Transaction = transaction;
            resolve.CommandText =
                """
                SELECT created_utc, message_id FROM messages
                WHERE scope_type = 'GROUP' AND scope_id = $group AND message_id = $id;
                """;
            resolve.Parameters.AddWithValue("$group", groupId.ToString("D"));
            resolve.Parameters.AddWithValue("$id", cursor.ToString("D"));
            await using var reader = await resolve.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new KeyNotFoundException("Group message cursor was not found.");
            afterCreated = reader.GetString(0);
            afterId = reader.GetString(1);
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT message_id, scope_type, scope_id, sender_user_id,
                   client_message_id, body, reply_to_message_id,
                   created_utc, edited_utc, deleted_utc
            FROM messages
            WHERE scope_type = 'GROUP' AND scope_id = $group
              AND (
                $afterCreated IS NULL
                OR created_utc > $afterCreated
                OR (created_utc = $afterCreated AND message_id > $afterId)
              )
            ORDER BY created_utc, message_id
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$group", groupId.ToString("D"));
        command.Parameters.AddWithValue("$afterCreated", (object?)afterCreated ?? DBNull.Value);
        command.Parameters.AddWithValue("$afterId", (object?)afterId ?? DBNull.Value);
        command.Parameters.AddWithValue("$limit", limit + 1);

        var items = new List<MessageResponse>();
        await using var pageReader = await command.ExecuteReaderAsync(cancellationToken);
        while (await pageReader.ReadAsync(cancellationToken))
            items.Add(ReadMessage(pageReader));
        await pageReader.DisposeAsync();
        transaction.Commit();

        var hasMore = items.Count > limit;
        var page = items.Take(limit).ToArray();
        return new MessagePageResponse(
            page, page.Length == 0 ? null : page[^1].MessageId, hasMore);
    }

    public async Task<IReadOnlyList<Guid>> GetCurrentRecipientsAsync(
        Guid actorUserId, Guid groupId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: true);
        await RequireActiveMemberAsync(connection, transaction, actorUserId, groupId, cancellationToken);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT gm.user_id
            FROM group_members gm
            JOIN users u ON u.user_id = gm.user_id
            WHERE gm.group_id = $group
              AND gm.removed_utc IS NULL
              AND u.disabled_utc IS NULL
            ORDER BY gm.user_id;
            """;
        command.Parameters.AddWithValue("$group", groupId.ToString("D"));
        var users = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            users.Add(Guid.Parse(reader.GetString(0)));
        await reader.DisposeAsync();
        transaction.Commit();
        return users;
    }

    private static async Task RequireActiveMemberAsync(
        SqliteConnection connection, SqliteTransaction transaction,
        Guid actor, Guid group, CancellationToken ct)
    {
        await using var check = connection.CreateCommand();
        check.Transaction = transaction;
        check.CommandText =
            """
            SELECT COUNT(1)
            FROM group_members gm
            JOIN users u ON u.user_id = gm.user_id
            JOIN groups g ON g.group_id = gm.group_id
            WHERE gm.group_id = $group AND gm.user_id = $actor
              AND gm.removed_utc IS NULL AND u.disabled_utc IS NULL;
            """;
        check.Parameters.AddWithValue("$group", group.ToString("D"));
        check.Parameters.AddWithValue("$actor", actor.ToString("D"));
        if (Convert.ToInt64(await check.ExecuteScalarAsync(ct)) != 1)
            throw new UnauthorizedAccessException("Active group membership is required.");
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

/// <summary>
/// Single-server, per-group delivery/removal serialization. A removal waits for
/// any in-flight delivery and future sends must re-read active group recipients.
/// </summary>
public sealed class GroupMessageDeliveryGate
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _groupLocks = new();

    public async Task<IDisposable> EnterAsync(Guid groupId, CancellationToken ct)
    {
        var semaphore = _groupLocks.GetOrAdd(groupId, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(ct);
        return new Releaser(semaphore);
    }

    private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        public void Dispose() => semaphore.Release();
    }
}
