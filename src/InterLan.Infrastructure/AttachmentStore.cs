using System.Security.Cryptography;
using InterLan.Contracts;
using Microsoft.Data.Sqlite;

namespace InterLan.Infrastructure;

/// <summary>
/// Scoped, fail-closed attachment storage. No caller can select a local path.
/// Stream to a private staging file; publish the final file before the database
/// transaction commits, so interrupted uploads never become downloadable rows.
/// </summary>
public sealed class AttachmentPayloadTooLargeException : ArgumentException
{
    public AttachmentPayloadTooLargeException()
        : base("Attachment exceeds the 10 MiB upload cap.") { }
}

public sealed class AttachmentStore
{
    public const long MaxUploadBytes = 10L * 1024 * 1024;
    public const long MaxScopeBytes = 100L * 1024 * 1024;
    private readonly SqliteDatabase _database;
    private readonly string _root;

    public AttachmentStore(SqliteDatabase database)
    {
        _database = database;
        _root = Path.Combine(
            Path.GetDirectoryName(database.DatabasePath) ??
                throw new InvalidOperationException("Database path has no parent directory."),
            "attachments");
    }

    /// <summary>
    /// Runs once during the single owner-server startup after migrations.
    /// Discards inaccessible crash leftovers; only exact server-minted names
    /// are eligible, and a referenced blob is never deleted.
    /// </summary>
    public async Task RecoverInterruptedUploadsAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(_root);
        var staging = Path.Combine(_root, ".staging");
        Directory.CreateDirectory(staging);

        // Startup takes place before accepting upload traffic. Only exact
        // UUID-generated staging filenames are considered cleanup candidates.
        foreach (var pending in Directory.EnumerateFiles(staging, "*.tmp"))
        {
            ct.ThrowIfCancellationRequested();
            if (Guid.TryParseExact(
                    Path.GetFileNameWithoutExtension(pending), "N", out _))
                File.Delete(pending);
        }

        await using var connection = _database.OpenConnection();
        foreach (var blob in Directory.EnumerateFiles(_root, "*.blob"))
        {
            ct.ThrowIfCancellationRequested();
            var idText = Path.GetFileNameWithoutExtension(blob);
            if (!Guid.TryParseExact(idText, "N", out var attachmentId))
                continue;

            await using var lookup = connection.CreateCommand();
            lookup.CommandText =
                """
                SELECT COUNT(1) FROM attachments
                WHERE attachment_id = $id AND local_cache_path = $filename;
                """;
            lookup.Parameters.AddWithValue("$id", attachmentId.ToString("D"));
            lookup.Parameters.AddWithValue("$filename", Path.GetFileName(blob));
            var referenced = Convert.ToInt64(
                await lookup.ExecuteScalarAsync(ct)) > 0;
            if (!referenced)
                File.Delete(blob);
        }
    }

    public AttachmentUploadPolicyResponse Policy() =>
        new(MaxUploadBytes, MaxScopeBytes,
            "Raw application/octet-stream body; filename supplied as X-InterLan-Filename display metadata.");

    public async Task<AttachmentResponse> SaveAsync(
        Guid actor, string scope, Guid scopeId,
        Stream content, string? filename, string? mediaType,
        CancellationToken ct = default)
    {
        ValidateScope(scope, scopeId);
        var displayName = NormalizeName(filename);
        var mime = NormalizeMime(mediaType);

        // An unauthorized caller must not be able to fill the staging disk.
        await using (var checkConnection = _database.OpenConnection())
            await RequireMemberAsync(checkConnection, null, actor, scope, scopeId, ct);

        var id = Guid.NewGuid();
        var staging = Path.Combine(_root, ".staging");
        Directory.CreateDirectory(staging);
        Directory.CreateDirectory(_root);
        var temporary = Path.Combine(staging, id.ToString("N") + ".tmp");
        var permanent = BlobPath(id);
        var filePublished = false;
        var databaseCommitted = false;
        long bytes = 0;
        var hash = string.Empty;

        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan
        };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        try
        {
            using (var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                await using var target = new FileStream(temporary, options);
                var buffer = new byte[64 * 1024];
                int read;
                while ((read = await content.ReadAsync(buffer, ct)) > 0)
                {
                    bytes += read;
                    if (bytes > MaxUploadBytes)
                        throw new AttachmentPayloadTooLargeException();
                    digest.AppendData(buffer, 0, read);
                    await target.WriteAsync(buffer.AsMemory(0, read), ct);
                }
                if (bytes == 0)
                    throw new ArgumentException("An empty attachment is not supported.");
                await target.FlushAsync(ct);
                hash = Convert.ToHexString(digest.GetHashAndReset());
                CryptographicOperations.ZeroMemory(buffer);
            }

            await using var connection = _database.OpenConnection();
            // Serialize quota decisions and the membership recheck against
            // contemporary group/DM mutations.
            using var transaction = connection.BeginTransaction(deferred: false);
            await RequireMemberAsync(connection, transaction, actor, scope, scopeId, ct);

            await using (var quota = connection.CreateCommand())
            {
                quota.Transaction = transaction;
                quota.CommandText =
                    """
                    SELECT COALESCE(SUM(byte_size), 0)
                    FROM attachments
                    WHERE scope_type = $scope AND scope_id = $scopeId;
                    """;
                quota.Parameters.AddWithValue("$scope", scope);
                quota.Parameters.AddWithValue("$scopeId", scopeId.ToString("D"));
                var used = Convert.ToInt64(await quota.ExecuteScalarAsync(ct));
                if (used > MaxScopeBytes - bytes)
                    throw new InvalidOperationException("Attachment quota for this conversation is exceeded.");
            }

            // The final filename is minted by the server and cannot contain
            // any client-selected path component. An orphan after process crash
            // is unreachable without a matching committed metadata record.
            File.Move(temporary, permanent, overwrite: false);
            filePublished = true;
            var createdUtc = DateTimeOffset.UtcNow;

            await using (var insert = connection.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText =
                    """
                    INSERT INTO attachments (
                        attachment_id, message_id, uploader_user_id,
                        original_filename, mime_type, byte_size, sha256,
                        local_cache_path, archive_state, created_utc,
                        scope_type, scope_id
                    ) VALUES (
                        $id, NULL, $actor,
                        $name, $mime, $bytes, $sha,
                        $cache, 'PENDING', $created,
                        $scope, $scopeId
                    );
                    """;
                insert.Parameters.AddWithValue("$id", id.ToString("D"));
                insert.Parameters.AddWithValue("$actor", actor.ToString("D"));
                insert.Parameters.AddWithValue("$name", displayName);
                insert.Parameters.AddWithValue("$mime", mime);
                insert.Parameters.AddWithValue("$bytes", bytes);
                insert.Parameters.AddWithValue("$sha", hash);
                insert.Parameters.AddWithValue("$cache", id.ToString("N") + ".blob");
                insert.Parameters.AddWithValue("$created", createdUtc.ToString("O"));
                insert.Parameters.AddWithValue("$scope", scope);
                insert.Parameters.AddWithValue("$scopeId", scopeId.ToString("D"));
                await insert.ExecuteNonQueryAsync(ct);
            }

            transaction.Commit();
            databaseCommitted = true;
            return new AttachmentResponse(
                id, scope, scopeId, actor, displayName, mime,
                bytes, hash, createdUtc, "PENDING");
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
            if (filePublished && !databaseCommitted && File.Exists(permanent))
                File.Delete(permanent);
        }
    }

    /// <summary>
    /// Lists committed attachments in one currently authorized scope. Cursor
    /// membership and list retrieval share a consistent SQLite read snapshot.
    /// Unscoped legacy rows remain inaccessible.
    /// </summary>
    public async Task<AttachmentPageResponse> ListAuthorizedAsync(
        Guid actor, string scope, Guid scopeId, Guid? beforeAttachmentId,
        int limit = 50, CancellationToken ct = default)
    {
        ValidateScope(scope, scopeId);
        if (limit is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(limit));

        await using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: true);
        await RequireMemberAsync(connection, transaction, actor, scope, scopeId, ct);

        string? beforeUtc = null;
        string? beforeId = null;
        if (beforeAttachmentId is { } cursor)
        {
            await using var lookup = connection.CreateCommand();
            lookup.Transaction = transaction;
            lookup.CommandText =
                """
                SELECT created_utc, attachment_id FROM attachments
                WHERE attachment_id = $cursor AND scope_type = $scope AND scope_id = $id;
                """;
            lookup.Parameters.AddWithValue("$cursor", cursor.ToString("D"));
            lookup.Parameters.AddWithValue("$scope", scope);
            lookup.Parameters.AddWithValue("$id", scopeId.ToString("D"));
            await using var reader = await lookup.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                throw new KeyNotFoundException("Attachment cursor not found in scope.");
            beforeUtc = reader.GetString(0);
            beforeId = reader.GetString(1);
        }

        await using var query = connection.CreateCommand();
        query.Transaction = transaction;
        query.CommandText =
            """
            SELECT attachment_id, scope_type, scope_id, uploader_user_id,
                   original_filename, mime_type, byte_size, sha256,
                   created_utc, archive_state
            FROM attachments
            WHERE scope_type = $scope AND scope_id = $id
              AND ($beforeUtc IS NULL OR created_utc < $beforeUtc
                   OR (created_utc = $beforeUtc AND attachment_id < $beforeId))
            ORDER BY created_utc DESC, attachment_id DESC
            LIMIT $limit;
            """;
        query.Parameters.AddWithValue("$scope", scope);
        query.Parameters.AddWithValue("$id", scopeId.ToString("D"));
        query.Parameters.AddWithValue("$beforeUtc", (object?)beforeUtc ?? DBNull.Value);
        query.Parameters.AddWithValue("$beforeId", (object?)beforeId ?? DBNull.Value);
        query.Parameters.AddWithValue("$limit", limit + 1);

        var items = new List<AttachmentResponse>();
        await using var page = await query.ExecuteReaderAsync(ct);
        while (await page.ReadAsync(ct))
        {
            items.Add(new AttachmentResponse(
                Guid.Parse(page.GetString(0)),
                page.GetString(1),
                Guid.Parse(page.GetString(2)),
                Guid.Parse(page.GetString(3)),
                page.GetString(4),
                page.GetString(5),
                page.GetInt64(6),
                page.GetString(7),
                DateTimeOffset.Parse(page.GetString(8)),
                page.GetString(9)));
        }
        await page.DisposeAsync();
        transaction.Commit();

        var hasMore = items.Count > limit;
        var result = items.Take(limit).ToArray();
        return new AttachmentPageResponse(
            result, hasMore && result.Length > 0 ? result[^1].AttachmentId : null, hasMore);
    }

    public async Task<AttachmentResponse> GetAuthorizedAsync(
        Guid actor, Guid attachmentId, CancellationToken ct = default)
    {
        if (attachmentId == Guid.Empty)
            throw new KeyNotFoundException("Attachment not found.");

        await using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: true);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT attachment_id, scope_type, scope_id, uploader_user_id,
                   original_filename, mime_type, byte_size, sha256,
                   created_utc, archive_state
            FROM attachments
            WHERE attachment_id = $id;
            """;
        command.Parameters.AddWithValue("$id", attachmentId.ToString("D"));
        AttachmentResponse record;
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct) ||
                reader.IsDBNull(1) || reader.IsDBNull(2))
                throw new KeyNotFoundException("Attachment not found.");

            record = new AttachmentResponse(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                Guid.Parse(reader.GetString(2)),
                Guid.Parse(reader.GetString(3)),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetInt64(6),
                reader.GetString(7),
                DateTimeOffset.Parse(reader.GetString(8)),
                reader.GetString(9));
        }

        await RequireMemberAsync(connection, transaction,
            actor, record.ScopeType, record.ScopeId, ct);
        transaction.Commit();
        return record;
    }

    public async Task<(AttachmentResponse Metadata, FileStream Content)> OpenReadAsync(
        Guid actor, Guid attachmentId, CancellationToken ct = default)
    {
        var metadata = await GetAuthorizedAsync(actor, attachmentId, ct);
        var path = BlobPath(metadata.AttachmentId);
        if (!File.Exists(path))
            throw new FileNotFoundException("Attachment bytes are not available.");

        var content = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return (metadata, content);
    }

    private string BlobPath(Guid id) => Path.Combine(_root, id.ToString("N") + ".blob");

    private static void ValidateScope(string scope, Guid scopeId)
    {
        if (scope is not ("DIRECT" or "GROUP") || scopeId == Guid.Empty)
            throw new ArgumentException("Valid conversation or group scope is required.");
    }

    private static string NormalizeName(string? filename)
    {
        var normalized = (filename ?? "attachment")
            .Replace('\\', '/').Split('/').Last().Trim();
        if (normalized.Length == 0)
            normalized = "attachment";
        if (normalized.Length > 120 ||
            normalized.Any(char.IsControl))
            throw new ArgumentException("Attachment display filename is invalid.");
        return normalized;
    }

    private static string NormalizeMime(string? mime)
    {
        var value = (mime ?? "application/octet-stream").Split(';')[0].Trim();
        if (value.Length is < 3 or > 120 ||
            value.Any(c => char.IsControl(c) || c == '\\'))
            throw new ArgumentException("Attachment MIME type is invalid.");
        return value;
    }

    private static async Task RequireMemberAsync(
        SqliteConnection connection, SqliteTransaction? transaction,
        Guid actor, string scope, Guid scopeId, CancellationToken ct)
    {
        ValidateScope(scope, scopeId);
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = scope == "GROUP"
            ? """
              SELECT COUNT(1)
              FROM group_members gm
              JOIN users u ON u.user_id = gm.user_id
              WHERE gm.group_id = $scopeId AND gm.user_id = $actor
                AND gm.removed_utc IS NULL AND u.disabled_utc IS NULL;
              """
            : """
              SELECT COUNT(1)
              FROM direct_conversation_members dm
              JOIN users u ON u.user_id = dm.user_id
              WHERE dm.conversation_id = $scopeId AND dm.user_id = $actor
                AND u.disabled_utc IS NULL;
              """;
        cmd.Parameters.AddWithValue("$scopeId", scopeId.ToString("D"));
        cmd.Parameters.AddWithValue("$actor", actor.ToString("D"));
        if (Convert.ToInt64(await cmd.ExecuteScalarAsync(ct)) != 1)
            throw new UnauthorizedAccessException("Current conversation membership is required.");
    }
}
