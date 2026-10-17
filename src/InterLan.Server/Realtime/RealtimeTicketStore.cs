using System.Collections.Concurrent;
using InterLan.Application;
using InterLan.Contracts;
using InterLan.Infrastructure;

namespace InterLan.Server.Realtime;

public sealed class RealtimeTicketStore
{
    private sealed record TicketEntry(
        SessionPrincipal Principal,
        DateTimeOffset ExpiresUtc);

    private readonly ConcurrentDictionary<string, TicketEntry> _tickets =
        new(StringComparer.Ordinal);

    public RealtimeTicketResponse Issue(
        SessionPrincipal principal,
        TimeSpan lifetime)
    {
        ArgumentNullException.ThrowIfNull(principal);

        if (lifetime <= TimeSpan.Zero || lifetime > TimeSpan.FromMinutes(2))
            throw new ArgumentOutOfRangeException(nameof(lifetime));

        // A realtime ticket may not outlive its underlying authenticated
        // session, even if the requested ticket lifetime would allow it.
        var now = DateTimeOffset.UtcNow;
        if (principal.ExpiresUtc <= now)
            throw new UnauthorizedAccessException("Cannot issue a ticket for an expired session.");

        var ticket = SecretCodec.NewToken();
        var expiresUtc = now.Add(lifetime);
        if (expiresUtc > principal.ExpiresUtc)
            expiresUtc = principal.ExpiresUtc;

        var hash = SecretCodec.HashToken(ticket);

        if (!_tickets.TryAdd(hash, new TicketEntry(principal, expiresUtc)))
            throw new InvalidOperationException("Realtime ticket collision.");

        PruneExpired();

        return new RealtimeTicketResponse(ticket, expiresUtc);
    }

    public SessionPrincipal? Consume(string ticket)
    {
        if (string.IsNullOrWhiteSpace(ticket))
            return null;

        var hash = SecretCodec.HashToken(ticket);
        if (!_tickets.TryRemove(hash, out var entry))
            return null;

        if (entry.ExpiresUtc <= DateTimeOffset.UtcNow)
            return null;

        return entry.Principal;
    }

    private void PruneExpired()
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var pair in _tickets)
        {
            if (pair.Value.ExpiresUtc <= now)
                _tickets.TryRemove(pair.Key, out _);
        }
    }
}
