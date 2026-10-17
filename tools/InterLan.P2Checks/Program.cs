using InterLan.Server;
using Microsoft.AspNetCore.Http;
using System.Net;
using InterLan.Contracts;
using InterLan.Infrastructure;

var failures = new List<string>();

var rateContextA = new DefaultHttpContext();
rateContextA.Connection.RemoteIpAddress = IPAddress.Parse("192.168.10.20");
var rateContextB = new DefaultHttpContext();
rateContextB.Connection.RemoteIpAddress = IPAddress.Parse("192.168.10.21");

Check(
    RateLimitPartitionKeys.RemoteAddress(rateContextA) !=
    RateLimitPartitionKeys.RemoteAddress(rateContextB),
    "rate-limit IP partitions isolate distinct clients");

var authRateContextA = new DefaultHttpContext();
authRateContextA.Request.Headers.Authorization = "Bearer p2-rate-limit-token-a";
var authRateContextAReplay = new DefaultHttpContext();
authRateContextAReplay.Request.Headers.Authorization = "Bearer p2-rate-limit-token-a";

var authPartition = RateLimitPartitionKeys.AuthenticatedClient(authRateContextA);
Check(
    authPartition == RateLimitPartitionKeys.AuthenticatedClient(authRateContextAReplay) &&
    !authPartition.Contains("p2-rate-limit-token-a", StringComparison.Ordinal),
    "authenticated rate-limit partition is stable without exposing bearer token");

var authRateContextB = new DefaultHttpContext();
authRateContextB.Request.Headers.Authorization = "Bearer p2-rate-limit-token-b";
Check(
    authPartition != RateLimitPartitionKeys.AuthenticatedClient(authRateContextB),
    "authenticated rate-limit partitions isolate distinct sessions");

void Check(bool condition, string name)
{
    Console.WriteLine($"{(condition ? "PASS" : "FAIL")} {name}");
    if (!condition) failures.Add(name);
}

async Task<bool> ThrowsAsync<T>(Func<Task> action) where T : Exception
{
    try
    {
        await action();
        return false;
    }
    catch (T)
    {
        return true;
    }
}

var root = Path.Combine(Path.GetTempPath(), "interlan-p2-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);

try
{
    var database = new SqliteDatabase(Path.Combine(root, "p2.db"));
    await database.InitializeAsync();

    await using (var connection = database.OpenConnection())
    {
        await using var journal = connection.CreateCommand();
        journal.CommandText = "PRAGMA journal_mode;";
        var journalMode = Convert.ToString(await journal.ExecuteScalarAsync());
        Check(string.Equals(journalMode, "wal", StringComparison.OrdinalIgnoreCase),
            "SQLite uses WAL journal mode");

        await using var timeout = connection.CreateCommand();
        timeout.CommandText = "PRAGMA busy_timeout;";
        var busyTimeout = Convert.ToInt32(await timeout.ExecuteScalarAsync());
        Check(busyTimeout >= 5000, "SQLite connection enforces busy timeout");
    }

    var alice = Guid.NewGuid();
    var bob = Guid.NewGuid();
    var carol = Guid.NewGuid();

    await using (var connection = database.OpenConnection())
    {
        foreach (var user in new[]
        {
            (alice, "alice", "Alice"),
            (bob, "bob", "Bob"),
            (carol, "carol", "Carol")
        })
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO users (user_id, username, display_name, role, created_utc)
                VALUES ($id, $username, $display, 'MEMBER', $utc);
                """;
            command.Parameters.AddWithValue("$id", user.Item1.ToString("D"));
            command.Parameters.AddWithValue("$username", user.Item2);
            command.Parameters.AddWithValue("$display", user.Item3);
            command.Parameters.AddWithValue("$utc", DateTimeOffset.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync();
        }
    }

    var chat = new ChatStore(database);

    var users = await chat.ListUsersAsync(alice);
    Check(users.Count == 3, "authenticated user directory returns active users");

    var directorySearch = await chat.SearchUsersAsync(
        alice,
        "bob",
        10);

    Check(
        directorySearch.Users.Count == 1 &&
        directorySearch.Users[0].UserId == bob,
        "user directory search returns active matching peer");

    var ab1 = await chat.GetOrCreateDirectConversationAsync(alice, bob);
    var ab2 = await chat.GetOrCreateDirectConversationAsync(bob, alice);
    Check(ab1.ConversationId == ab2.ConversationId, "same user pair resolves to one canonical direct conversation");

    var concurrentConversationIds = await Task.WhenAll(
        Enumerable.Range(0, 16)
            .Select(index => index % 2 == 0
                ? chat.GetOrCreateDirectConversationAsync(alice, bob)
                : chat.GetOrCreateDirectConversationAsync(bob, alice)));

    Check(
        concurrentConversationIds.All(conversation => conversation.ConversationId == ab1.ConversationId),
        "concurrent direct-conversation creation resolves to one canonical conversation");

    var ac = await chat.GetOrCreateDirectConversationAsync(alice, carol);
    Check(ac.ConversationId != ab1.ConversationId, "different pair gets different conversation");

    var bc = await chat.GetOrCreateDirectConversationAsync(bob, carol);

    var normalizedMessage = await chat.SendDirectMessageAsync(
        bob,
        bc.ConversationId,
        new SendMessageRequest(
            Guid.NewGuid(),
            "  Cafe\u0301  "));

    Check(
        normalizedMessage.Message.Body == "Café",
        "message text is trimmed and normalized to Unicode NFC");

    Check(await ThrowsAsync<ArgumentException>(() =>
        chat.SendDirectMessageAsync(
            bob,
            bc.ConversationId,
            new SendMessageRequest(
                Guid.NewGuid(),
                "unsafe\u0000message"))),
        "unsupported control characters fail closed");

    var listed = await chat.ListDirectConversationsAsync(alice);
    Check(listed.Count == 2, "direct conversation listing is membership scoped");

    var client1 = Guid.NewGuid();
    var first = await chat.SendDirectMessageAsync(
        alice,
        ab1.ConversationId,
        new SendMessageRequest(client1, "hello bob"));

    var duplicate = await chat.SendDirectMessageAsync(
        alice,
        ab1.ConversationId,
        new SendMessageRequest(client1, "hello bob"));

    Check(first.Created && !duplicate.Created && first.Message.MessageId == duplicate.Message.MessageId,
        "duplicate client message ID is idempotent");

    await Task.Delay(2);

    var second = await chat.SendDirectMessageAsync(
        bob,
        ab1.ConversationId,
        new SendMessageRequest(Guid.NewGuid(), "hello alice"));

    var history = await chat.GetDirectHistoryAsync(alice, ab1.ConversationId, null, 100);
    Check(history.Count == 2 && history[0].MessageId == first.Message.MessageId &&
          history[1].MessageId == second.Message.MessageId,
        "direct history is ordered and durable");

    var catchup = await chat.GetDirectHistoryAsync(alice, ab1.ConversationId, first.Message.MessageId, 100);
    Check(catchup.Count == 1 && catchup[0].MessageId == second.Message.MessageId,
        "message cursor returns reconnect catch-up without duplicates");

    Check(await ThrowsAsync<UnauthorizedAccessException>(() =>
        chat.GetDirectHistoryAsync(carol, ab1.ConversationId, null, 100)),
        "non-member cannot read another direct conversation");

    Check(await ThrowsAsync<UnauthorizedAccessException>(() =>
        chat.SendDirectMessageAsync(
            carol,
            ab1.ConversationId,
            new SendMessageRequest(Guid.NewGuid(), "intrusion"))),
        "non-member cannot send into another direct conversation");

    Check(await ThrowsAsync<ArgumentException>(() =>
        chat.SendDirectMessageAsync(
            alice,
            ab1.ConversationId,
            new SendMessageRequest(Guid.NewGuid(), new string('x', ChatStore.MaxMessageLength + 1)))),
        "oversized message fails closed");

    Check(await ThrowsAsync<InvalidOperationException>(() =>
        chat.MarkDeliveredAsync(alice, first.Message.MessageId)),
        "sender cannot acknowledge own message as delivered");

    Check(await ThrowsAsync<InvalidOperationException>(() =>
        chat.MarkReadAsync(alice, first.Message.MessageId)),
        "sender cannot acknowledge own message as read");

    await chat.MarkDeliveredAsync(bob, first.Message.MessageId);
    await chat.MarkDeliveredAsync(bob, first.Message.MessageId);

    var deliveredReceipts = await chat.GetReceiptsAsync(alice, first.Message.MessageId);
    Check(
        deliveredReceipts.Count == 1 &&
        deliveredReceipts[0].UserId == bob &&
        deliveredReceipts[0].DeliveredUtc is not null &&
        deliveredReceipts[0].ReadUtc is null,
        "recipient delivery acknowledgement is durable and idempotent");

    var aliceDefaultPreference =
        await chat.GetDirectConversationPreferenceAsync(
            alice,
            ab1.ConversationId);

    Check(
        !aliceDefaultPreference.IsPinned &&
        aliceDefaultPreference.MutedUntilUtc is null &&
        !aliceDefaultPreference.IsArchived,
        "direct conversation preference defaults are neutral");

    var bobMuteUntil = DateTimeOffset.UtcNow.AddMinutes(30);
    var bobPreference = await chat.UpdateDirectConversationPreferenceAsync(
        bob,
        ab1.ConversationId,
        new UpdateDirectConversationPreferenceRequest(
            IsPinned: true,
            MutedUntilUtc: bobMuteUntil,
            IsArchived: false));

    Check(
        bobPreference.IsPinned &&
        bobPreference.MutedUntilUtc is not null &&
        !bobPreference.IsArchived,
        "direct conversation preference persists pin and mute state");

    await chat.UpdateDirectConversationPreferenceAsync(
        bob,
        ab1.ConversationId,
        new UpdateDirectConversationPreferenceRequest(
            IsPinned: true,
            MutedUntilUtc: bobMuteUntil,
            IsArchived: true));

    var bobVisibleOnly = await chat.ListDirectConversationSummariesAsync(
        bob,
        includeArchived: false);

    Check(
        bobVisibleOnly.All(summary =>
            summary.ConversationId != ab1.ConversationId),
        "archived direct conversation is omitted from active summary list");

    Check(await ThrowsAsync<UnauthorizedAccessException>(() =>
        chat.UpdateDirectConversationPreferenceAsync(
            carol,
            ab1.ConversationId,
            new UpdateDirectConversationPreferenceRequest(
                IsPinned: true,
                MutedUntilUtc: null,
                IsArchived: false))),
        "non-member cannot mutate another direct conversation preference");

    var pastMutePreference = await chat.UpdateDirectConversationPreferenceAsync(
        alice,
        ab1.ConversationId,
        new UpdateDirectConversationPreferenceRequest(
            IsPinned: false,
            MutedUntilUtc: DateTimeOffset.UtcNow.AddMinutes(-5),
            IsArchived: false));

    Check(
        pastMutePreference.MutedUntilUtc is null,
        "expired mute request normalizes to unmuted state");

    var aliceAfterBobPreference =
        await chat.GetDirectConversationPreferenceAsync(
            alice,
            ab1.ConversationId);

    Check(
        !aliceAfterBobPreference.IsPinned &&
        aliceAfterBobPreference.MutedUntilUtc is null,
        "direct conversation preferences remain isolated per user");

    var bobUnreadAggregate =
        await chat.GetDirectUnreadSummaryAsync(bob);

    Check(
        bobUnreadAggregate.TotalUnreadMessages == 1 &&
        bobUnreadAggregate.ConversationsWithUnread == 1,
        "direct unread aggregate tracks unread messages and conversations");

    await chat.UpdateDirectConversationPreferenceAsync(
        bob,
        ab1.ConversationId,
        new UpdateDirectConversationPreferenceRequest(
            IsPinned: true,
            MutedUntilUtc: bobMuteUntil,
            IsArchived: false));

    var bobBeforeReadSummaries =
        await chat.ListDirectConversationSummariesAsync(bob);
    var bobBeforeRead = bobBeforeReadSummaries.Single(summary =>
        summary.ConversationId == ab1.ConversationId);

    Check(
        bobBeforeRead.IsPinned &&
        bobBeforeRead.MutedUntilUtc is not null,
        "conversation summary projects user preference state");
    Check(
        bobBeforeRead.UnreadCount == 1 &&
        bobBeforeRead.LastMessage?.MessageId == second.Message.MessageId,
        "conversation summary projects last message and recipient unread count");

    await chat.MarkReadAsync(bob, first.Message.MessageId);
    await chat.MarkReadAsync(bob, first.Message.MessageId);

    var readReceipts = await chat.GetReceiptsAsync(alice, first.Message.MessageId);
    Check(
        readReceipts.Count == 1 &&
        readReceipts[0].UserId == bob &&
        readReceipts[0].DeliveredUtc is not null &&
        readReceipts[0].ReadUtc is not null,
        "recipient read acknowledgement advances receipt state idempotently");

    var bobAfterReadSummaries =
        await chat.ListDirectConversationSummariesAsync(bob);
    var bobAfterRead = bobAfterReadSummaries.Single(summary =>
        summary.ConversationId == ab1.ConversationId);
    Check(
        bobAfterRead.UnreadCount == 0,
        "read acknowledgement clears direct-conversation unread projection");

    var mutable = await chat.SendDirectMessageAsync(
        alice,
        ac.ConversationId,
        new SendMessageRequest(Guid.NewGuid(), "draft message"));

    Check(await ThrowsAsync<UnauthorizedAccessException>(() =>
        chat.EditDirectMessageAsync(
            carol,
            ac.ConversationId,
            mutable.Message.MessageId,
            new EditMessageRequest("recipient cannot edit"))),
        "direct-message recipient cannot edit sender content");

    var edited = await chat.EditDirectMessageAsync(
        alice,
        ac.ConversationId,
        mutable.Message.MessageId,
        new EditMessageRequest("edited message"));

    Check(
        edited.Body == "edited message" &&
        edited.EditedUtc is not null,
        "sender can edit active direct message");

    Check(await ThrowsAsync<UnauthorizedAccessException>(() =>
        chat.DeleteDirectMessageAsync(
            carol,
            ac.ConversationId,
            mutable.Message.MessageId)),
        "direct-message recipient cannot delete sender content");

    var deleted = await chat.DeleteDirectMessageAsync(
        alice,
        ac.ConversationId,
        mutable.Message.MessageId);

    var deletedAgain = await chat.DeleteDirectMessageAsync(
        alice,
        ac.ConversationId,
        mutable.Message.MessageId);

    Check(
        deleted.DeletedUtc == deletedAgain.DeletedUtc,
        "sender direct-message delete is idempotent");

    var afterDelete = await chat.GetDirectHistoryAsync(
        carol,
        ac.ConversationId,
        null,
        100);

    Check(
        afterDelete.Count == 1 &&
        afterDelete[0].MessageId == mutable.Message.MessageId &&
        afterDelete[0].DeletedUtc is not null &&
        string.IsNullOrEmpty(afterDelete[0].Body),
        "soft-deleted direct message remains as a redacted history tombstone");

    var mutableDetail = await chat.GetDirectMessageByIdAsync(
        alice,
        ac.ConversationId,
        mutable.Message.MessageId);

    Check(
        mutableDetail.MessageId == mutable.Message.MessageId,
        "authorized direct-message detail resolves durable message");

    var concurrentSends = await Task.WhenAll(
        Enumerable.Range(0, 20)
            .Select(index => chat.SendDirectMessageAsync(
                alice,
                ac.ConversationId,
                new SendMessageRequest(Guid.NewGuid(), $"concurrent-{index:D2}"))));

    Check(
        concurrentSends.Count(result => result.Created) == 20,
        "concurrent unique sends all persist exactly once");

    var concurrentHistory = await chat.GetDirectHistoryAsync(
        carol,
        ac.ConversationId,
        null,
        100);

    Check(
        concurrentHistory.Count == 21,
        "concurrent unique sends remain fully readable");

    var firstPage = await chat.GetDirectHistoryPageAsync(
        carol,
        ac.ConversationId,
        null,
        5);

    var secondPage = await chat.GetDirectHistoryPageAsync(
        carol,
        ac.ConversationId,
        firstPage.NextAfterMessageId,
        5);

    Check(
        firstPage.Items.Count == 5 &&
        firstPage.HasMore &&
        secondPage.Items.Count == 5 &&
        !firstPage.Items.Select(message => message.MessageId)
            .Intersect(secondPage.Items.Select(message => message.MessageId))
            .Any(),
        "bounded cursor pages advance without overlap");

    var duplicateClientMessageId = Guid.NewGuid();
    var duplicateRace = await Task.WhenAll(
        Enumerable.Range(0, 12)
            .Select(_ => chat.SendDirectMessageAsync(
                alice,
                ac.ConversationId,
                new SendMessageRequest(duplicateClientMessageId, "same-idempotent-payload"))));

    Check(
        duplicateRace.Count(result => result.Created) == 1 &&
        duplicateRace.Select(result => result.Message.MessageId).Distinct().Count() == 1,
        "concurrent duplicate client message IDs collapse to one durable message");

    Check(await ThrowsAsync<InvalidOperationException>(() =>
        chat.SendDirectMessageAsync(
            alice,
            ac.ConversationId,
            new SendMessageRequest(
                duplicateClientMessageId,
                "different-payload-for-same-idempotency-key"))),
        "idempotency key replay with different content fails closed");

    var afterDuplicateRace = await chat.GetDirectHistoryAsync(
        carol,
        ac.ConversationId,
        null,
        100);

    Check(
        afterDuplicateRace.Count == 22,
        "concurrent duplicate race adds exactly one message");

    Check(await ThrowsAsync<UnauthorizedAccessException>(() =>
        chat.SearchDirectMessagesAsync(
            bob,
            ac.ConversationId,
            "concurrent",
            20)),
        "non-member cannot search another direct conversation");

    var aliceGlobalSearch = await chat.SearchAllDirectMessagesAsync(
        alice,
        "concurrent-0",
        100);

    Check(
        aliceGlobalSearch.Hits.Count > 0 &&
        aliceGlobalSearch.Hits.All(hit =>
            hit.ConversationId == ac.ConversationId),
        "global direct search returns only authorized matching conversations");

    var bobGlobalSearch = await chat.SearchAllDirectMessagesAsync(
        bob,
        "concurrent-0",
        100);

    Check(
        bobGlobalSearch.Hits.Count == 0,
        "global direct search does not leak another conversation");

    var searchResult = await chat.SearchDirectMessagesAsync(
        carol,
        ac.ConversationId,
        "concurrent-0",
        50);

    Check(
        searchResult.Items.Count > 0 &&
        searchResult.Items.All(message =>
            message.Body.Contains(
                "concurrent-0",
                StringComparison.OrdinalIgnoreCase)),
        "authorized direct-message search returns scoped matching messages");

    var recentPage = await chat.GetDirectRecentHistoryPageAsync(
        carol,
        ac.ConversationId,
        null,
        5);

    var olderPage = await chat.GetDirectRecentHistoryPageAsync(
        carol,
        ac.ConversationId,
        recentPage.OlderBeforeMessageId,
        5);

    Check(
        recentPage.Items.Count == 5 &&
        recentPage.HasOlder &&
        olderPage.Items.Count == 5 &&
        !recentPage.Items.Select(message => message.MessageId)
            .Intersect(olderPage.Items.Select(message => message.MessageId))
            .Any(),
        "recent-history pages walk backward without overlap");

    var carolUnreadBeforeAdvance =
        (await chat.ListDirectConversationSummariesAsync(carol))
        .Single(summary => summary.ConversationId == ac.ConversationId);

    Check(
        carolUnreadBeforeAdvance.UnreadCount == 21,
        "conversation unread projection excludes deleted tombstones");

    var partialRead = await chat.MarkConversationReadAsync(
        carol,
        ac.ConversationId,
        firstPage.NextAfterMessageId);

    Check(
        partialRead.MarkedCount == 4,
        "conversation read cursor advances only active recipient messages");

    var carolUnreadAfterPartial =
        (await chat.ListDirectConversationSummariesAsync(carol))
        .Single(summary => summary.ConversationId == ac.ConversationId);

    Check(
        carolUnreadAfterPartial.UnreadCount == 17,
        "partial conversation read advance preserves later unread messages");

    var fullRead = await chat.MarkConversationReadAsync(
        carol,
        ac.ConversationId,
        upToMessageId: null);

    Check(
        fullRead.MarkedCount == 17,
        "conversation read-all advances remaining recipient messages");

    var carolUnreadAfterFull =
        (await chat.ListDirectConversationSummariesAsync(carol))
        .Single(summary => summary.ConversationId == ac.ConversationId);

    Check(
        carolUnreadAfterFull.UnreadCount == 0,
        "conversation read-all clears unread projection");

    var reopened = new SqliteDatabase(Path.Combine(root, "p2.db"));
    await reopened.InitializeAsync();
    var reopenedChat = new ChatStore(reopened);
    var afterRestart = await reopenedChat.GetDirectHistoryAsync(alice, ab1.ConversationId, null, 100);
    Check(afterRestart.Count == 2, "message history survives database restart");
}
finally
{
    try { Directory.Delete(root, true); } catch { }
}

if (failures.Count > 0)
{
    Console.Error.WriteLine($"P2 checks failed: {string.Join(", ", failures)}");
    return 1;
}

Console.WriteLine("INTER-LAN P2 DIRECT MESSAGING CHECKS: PASS");
return 0;
