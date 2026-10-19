using InterLan.Application;
using InterLan.Contracts;
using InterLan.Infrastructure;
using Microsoft.AspNetCore.SignalR;

namespace InterLan.Server.Realtime;

public sealed class ChatHub(
    EnrollmentStore enrollment,
    ChatStore chat,
    RealtimeConnectionRegistry connections,
    RealtimeTicketStore tickets,
    TypingDispatchGate typing,
    ILogger<ChatHub> logger) : Hub
{
    private const string LeaseKey = "interlan-realtime-lease";

    public const string AuthenticatedGroup = "authenticated";
    public static string UserGroup(Guid userId) => $"user:{userId:D}";

    public override async Task OnConnectedAsync()
    {
        var principal = await ResolvePrincipalAsync(Context.GetHttpContext());

        if (principal is null)
        {
            Context.Abort();
            return;
        }

        // Register first, then re-check durable session authority. A revoke
        // that races between ticket validation and registry insertion will be
        // caught here; a revoke after insertion invokes the registered abort.
        Context.Items["principal"] = principal;
        var lease = connections.Register(
            principal,
            Context.ConnectionId,
            Context.Abort);
        Context.Items[LeaseKey] = lease;
        var currentPrincipal = await enrollment.ValidateSessionByIdAsync(
            principal.SessionId,
            Context.ConnectionAborted);
        if (currentPrincipal is null ||
            currentPrincipal.UserId != principal.UserId ||
            currentPrincipal.DeviceId != principal.DeviceId)
        {
            lease.Dispose();
            Context.Items.Remove(LeaseKey);
            Context.Items.Remove("principal");
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(
            Context.ConnectionId,
            UserGroup(principal.UserId));
        await Groups.AddToGroupAsync(
            Context.ConnectionId,
            AuthenticatedGroup);

        await Clients.GroupExcept(
                AuthenticatedGroup,
                new[] { Context.ConnectionId })
            .SendAsync(
                "PresenceChanged",
                new UserPresenceResponse(
                    principal.UserId,
                    IsOnline: true,
                    connections.GetUserConnectionCount(principal.UserId),
                    DateTimeOffset.UtcNow),
                Context.ConnectionAborted);

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var principal = Context.Items.TryGetValue("principal", out var principalValue)
            ? principalValue as SessionPrincipal
            : null;

        if (Context.Items.TryGetValue(LeaseKey, out var value) && value is IDisposable lease)
        {
            lease.Dispose();
            Context.Items.Remove(LeaseKey);
        }

        if (principal is not null)
        {
            // Only the last connection for this user ends the shared typing session.
            // Notify current DM members explicitly; never broadcast typing to all users.
            if (!connections.IsUserOnline(principal.UserId))
            {
                foreach (var conversationId in typing.ClearUser(principal.UserId))
                {
                    try
                    {
                        var members = await chat.GetDirectMemberIdsAsync(
                            principal.UserId,
                            conversationId,
                            CancellationToken.None);

                        await BroadcastTypingAsync(
                            conversationId,
                            principal.UserId,
                            isTyping: false,
                            members,
                            CancellationToken.None);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Membership may have been removed while the connection closed.
                    }
                    catch (Exception error)
                    {
                        logger.LogWarning(error, "Unable to clear expired typing state on disconnect.");
                    }
                }
            }

            await Clients.GroupExcept(
                    AuthenticatedGroup,
                    new[] { Context.ConnectionId })
                .SendAsync(
                    "PresenceChanged",
                    new UserPresenceResponse(
                        principal.UserId,
                        connections.IsUserOnline(principal.UserId),
                        connections.GetUserConnectionCount(principal.UserId),
                        DateTimeOffset.UtcNow));
        }

        await base.OnDisconnectedAsync(exception);
    }

    public async Task SetTyping(
        Guid conversationId,
        bool isTyping)
    {
        var principal = await RequireLivePrincipalAsync();

        IReadOnlyList<Guid> members;
        try
        {
            members = await chat.GetDirectMemberIdsAsync(
                principal.UserId,
                conversationId,
                Context.ConnectionAborted);
        }
        catch (UnauthorizedAccessException)
        {
            throw new HubException("Direct conversation access denied.");
        }

        // Critical ordering: membership is checked before the suppression cache.
        // Repeated typing=true calls are suppressed atomically for this user/DM.
        if (isTyping)
        {
            if (!typing.TryBeginTyping(principal.UserId, conversationId))
                return;
        }
        else
        {
            typing.StopTyping(principal.UserId, conversationId);
        }

        await BroadcastTypingAsync(
            conversationId,
            principal.UserId,
            isTyping,
            members,
            Context.ConnectionAborted);
    }

    private async Task BroadcastTypingAsync(
        Guid conversationId,
        Guid authorUserId,
        bool isTyping,
        IReadOnlyList<Guid> currentMembers,
        CancellationToken cancellationToken)
    {
        var payload = new TypingIndicatorResponse(
            conversationId,
            authorUserId,
            isTyping,
            DateTimeOffset.UtcNow);

        foreach (var memberId in currentMembers.Where(id => id != authorUserId))
        {
            await Clients.Group(UserGroup(memberId))
                .SendAsync("TypingChanged", payload, cancellationToken);
        }
    }

    public async Task<ControlPong> Ping()
    {
        await RequireLivePrincipalAsync();
        return new(ApiContractVersion.Current, DateTimeOffset.UtcNow, "ok");
    }

    private async Task<SessionPrincipal> RequireLivePrincipalAsync()
    {
        if (!Context.Items.TryGetValue("principal", out var cached) ||
            cached is not SessionPrincipal principal)
        {
            Context.Abort();
            throw new HubException("Authenticated session is required.");
        }

        // A cached SignalR principal is not ongoing authorization.
        // Consult canonical server state for every client-initiated hub call.
        var live = await enrollment.ValidateSessionByIdAsync(
            principal.SessionId,
            Context.ConnectionAborted);
        if (live is null ||
            live.UserId != principal.UserId ||
            live.DeviceId != principal.DeviceId)
        {
            Context.Abort();
            throw new HubException("Device or session access has been revoked.");
        }

        return live;
    }

    private async Task<SessionPrincipal?> ResolvePrincipalAsync(HttpContext? context)
    {
        if (context is null)
            return null;

        var authorization = context.Request.Headers.Authorization.ToString();
        if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var bearerToken = authorization["Bearer ".Length..].Trim();
            if (!string.IsNullOrWhiteSpace(bearerToken))
            {
                return await enrollment.ValidateSessionAsync(
                    bearerToken,
                    Context.ConnectionAborted);
            }
        }

        var realtimeTicket = context.Request.Query["ticket"].ToString();
        var ticketPrincipal = tickets.Consume(realtimeTicket);
        if (ticketPrincipal is null)
            return null;

        return await enrollment.ValidateSessionByIdAsync(
            ticketPrincipal.SessionId,
            Context.ConnectionAborted);
    }
}
