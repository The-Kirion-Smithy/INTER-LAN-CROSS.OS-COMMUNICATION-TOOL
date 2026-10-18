namespace InterLan.Server.Realtime;

/// <summary>
/// Ephemeral, process-local dispatch suppression for direct-conversation typing events.
/// Caller authorization must be checked by the hub before using this gate.
/// </summary>
public sealed class TypingDispatchGate
{
    private readonly object _sync = new();
    private readonly Dictionary<TypingKey, TypingEntry> _entries = new();
    private readonly TimeProvider _clock;
    private readonly TimeSpan _dispatchInterval;
    private readonly TimeSpan _idleLifetime;
    private readonly int _maximumEntries;
    private int _operations;

    public TypingDispatchGate(
        TimeProvider? clock = null,
        TimeSpan? dispatchInterval = null,
        TimeSpan? idleLifetime = null,
        int maximumEntries = 4096)
    {
        _clock = clock ?? TimeProvider.System;
        _dispatchInterval = dispatchInterval ?? TimeSpan.FromSeconds(2);
        _idleLifetime = idleLifetime ?? TimeSpan.FromSeconds(12);

        if (_dispatchInterval <= TimeSpan.Zero || _idleLifetime < _dispatchInterval)
            throw new ArgumentOutOfRangeException(nameof(dispatchInterval), "Typing lifetime must exceed the dispatch interval.");
        if (maximumEntries <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumEntries));

        _maximumEntries = maximumEntries;
    }

    /// <summary>
    /// Atomically reserves a typing=true broadcast for a verified member. A repeated
    /// true inside the suppression interval updates idle expiry but is not published.
    /// A fresh true after the interval is published even if the user is still typing.
    /// </summary>
    public bool TryBeginTyping(Guid authenticatedUserId, Guid conversationId)
    {
        var key = new TypingKey(authenticatedUserId, conversationId);
        lock (_sync)
        {
            var now = _clock.GetUtcNow();
            SweepIfDue(now);

            if (_entries.TryGetValue(key, out var existing) && existing.ExpiresUtc > now)
            {
                _entries[key] = existing with { ExpiresUtc = now + _idleLifetime };
                if (now < existing.NextDispatchUtc)
                    return false;

                _entries[key] = new TypingEntry(now + _dispatchInterval, now + _idleLifetime);
                return true;
            }

            _entries.Remove(key);
            if (_entries.Count >= _maximumEntries)
                EvictEarliestExpiry();

            _entries[key] = new TypingEntry(now + _dispatchInterval, now + _idleLifetime);
            return true;
        }
    }

    /// <summary>
    /// Clears suppression without delaying typing=false, which the hub always emits.
    /// </summary>
    public void StopTyping(Guid authenticatedUserId, Guid conversationId)
    {
        lock (_sync)
            _entries.Remove(new TypingKey(authenticatedUserId, conversationId));
    }

    /// <summary>
    /// Clears all conversations for the last disconnected connection of a user,
    /// returning conversation IDs for member-scoped typing=false notifications.
    /// </summary>
    public IReadOnlyList<Guid> ClearUser(Guid authenticatedUserId)
    {
        lock (_sync)
        {
            var ids = _entries.Keys
                .Where(key => key.UserId == authenticatedUserId)
                .Select(key => key.ConversationId)
                .ToArray();

            foreach (var conversationId in ids)
                _entries.Remove(new TypingKey(authenticatedUserId, conversationId));

            return ids;
        }
    }

    private void SweepIfDue(DateTimeOffset now)
    {
        // Neither entries nor expired/abandoned conversations can grow without bound.
        if (++_operations % 128 != 0 && _entries.Count < _maximumEntries)
            return;

        foreach (var key in _entries
                     .Where(pair => pair.Value.ExpiresUtc <= now)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            _entries.Remove(key);
        }
    }

    private void EvictEarliestExpiry()
    {
        TypingKey earliestKey = default;
        var earliestExpiry = DateTimeOffset.MaxValue;

        foreach (var (key, entry) in _entries)
        {
            if (entry.ExpiresUtc >= earliestExpiry)
                continue;

            earliestKey = key;
            earliestExpiry = entry.ExpiresUtc;
        }

        _entries.Remove(earliestKey);
    }

    private readonly record struct TypingKey(Guid UserId, Guid ConversationId);
    private readonly record struct TypingEntry(DateTimeOffset NextDispatchUtc, DateTimeOffset ExpiresUtc);
}
