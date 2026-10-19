using InterLan.Contracts;
using InterLan.Infrastructure;
using InterLan.Server.Realtime;
using Microsoft.AspNetCore.SignalR;

namespace InterLan.Server;

public static class GroupEndpointMappings
{
    public static IEndpointRouteBuilder MapInterLanGroupEndpoints(
        this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/groups", async (
            CreateGroupRequest request,
            HttpContext context,
            EnrollmentStore enrollment,
            GroupStore groups,
            CancellationToken cancellationToken) =>
        {
            var principal = await AuthorizationHelpers.GetPrincipalAsync(
                context,
                enrollment,
                cancellationToken);
            if (principal is null)
                return Results.Unauthorized();

            try
            {
                return Results.Ok(await groups.CreateGroupAsync(
                    principal.UserId,
                    request,
                    cancellationToken));
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
        });

        app.MapGet("/api/v1/groups", async (
            HttpContext context,
            EnrollmentStore enrollment,
            GroupStore groups,
            CancellationToken cancellationToken) =>
        {
            var principal = await AuthorizationHelpers.GetPrincipalAsync(
                context,
                enrollment,
                cancellationToken);
            if (principal is null)
                return Results.Unauthorized();

            return Results.Ok(await groups.ListGroupsAsync(
                principal.UserId,
                cancellationToken));
        });

        app.MapGet("/api/v1/groups/{groupId:guid}", async (
            Guid groupId,
            HttpContext context,
            EnrollmentStore enrollment,
            GroupStore groups,
            CancellationToken cancellationToken) =>
        {
            var principal = await AuthorizationHelpers.GetPrincipalAsync(
                context,
                enrollment,
                cancellationToken);
            if (principal is null)
                return Results.Unauthorized();

            try
            {
                return Results.Ok(await groups.GetGroupDetailsAsync(
                    principal.UserId,
                    groupId,
                    cancellationToken));
            }
            catch (UnauthorizedAccessException)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        });

        app.MapPut("/api/v1/groups/{groupId:guid}", async (
            Guid groupId,
            UpdateGroupRequest request,
            HttpContext context,
            EnrollmentStore enrollment,
            GroupStore groups,
            CancellationToken cancellationToken) =>
        {
            var principal = await AuthorizationHelpers.GetPrincipalAsync(
                context,
                enrollment,
                cancellationToken);
            if (principal is null)
                return Results.Unauthorized();

            try
            {
                return Results.Ok(await groups.UpdateGroupAsync(
                    principal.UserId,
                    groupId,
                    request,
                    cancellationToken));
            }
            catch (UnauthorizedAccessException)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
        });

        app.MapGet("/api/v1/groups/{groupId:guid}/events", async (
            Guid groupId,
            Guid? afterEventId,
            int? limit,
            HttpContext context,
            EnrollmentStore enrollment,
            GroupStore groups,
            CancellationToken cancellationToken) =>
        {
            var principal = await AuthorizationHelpers.GetPrincipalAsync(
                context,
                enrollment,
                cancellationToken);
            if (principal is null)
                return Results.Unauthorized();

            try
            {
                return Results.Ok(await groups.ListGroupEventsAsync(
                    principal.UserId,
                    groupId,
                    afterEventId,
                    Math.Clamp(limit ?? 100, 1, 250),
                    cancellationToken));
            }
            catch (UnauthorizedAccessException)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        });

        app.MapPost("/api/v1/groups/{groupId:guid}/members", async (
            Guid groupId,
            AddGroupMemberRequest request,
            HttpContext context,
            EnrollmentStore enrollment,
            GroupStore groups,
            CancellationToken cancellationToken) =>
        {
            var principal = await AuthorizationHelpers.GetPrincipalAsync(
                context,
                enrollment,
                cancellationToken);
            if (principal is null)
                return Results.Unauthorized();

            try
            {
                return Results.Ok(await groups.AddMemberAsync(
                    principal.UserId,
                    groupId,
                    request,
                    cancellationToken));
            }
            catch (UnauthorizedAccessException)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (InvalidOperationException exception)
            {
                return Results.Conflict(new { error = exception.Message });
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
        });

        app.MapDelete("/api/v1/groups/{groupId:guid}/members/{userId:guid}", async (
            Guid groupId,
            Guid userId,
            HttpContext context,
            EnrollmentStore enrollment,
            GroupStore groups,
            GroupMessageDeliveryGate gate,
            IHubContext<ChatHub> hub,
            ILoggerFactory loggers,
            CancellationToken cancellationToken) =>
        {
            var principal = await AuthorizationHelpers.GetPrincipalAsync(
                context,
                enrollment,
                cancellationToken);
            if (principal is null)
                return Results.Unauthorized();

            try
            {
                // Preserve the removal cutoff: the group delivery gate cannot
                // release until the last authorized in-flight fan-out has ended.
                using var dispatchLease = await gate.EnterAsync(groupId, cancellationToken);
                var removed = await groups.RemoveMemberAsync(
                    principal.UserId,
                    groupId,
                    userId,
                    cancellationToken);
                try
                {
                    await hub.Clients.Group(ChatHub.UserGroup(userId))
                        .SendAsync("GroupMembershipRevoked", new { GroupId = groupId },
                            cancellationToken);
                }
                catch (Exception notificationError) when (notificationError is not OperationCanceledException)
                {
                    loggers.CreateLogger("GroupMembership").LogWarning(
                        notificationError, "Group membership invalidation notice could not be dispatched.");
                    // Committed membership revocation remains authoritative.
                }
                return Results.Ok(removed);
            }
            catch (UnauthorizedAccessException)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (InvalidOperationException exception)
            {
                return Results.Conflict(new { error = exception.Message });
            }
        });

        app.MapPut("/api/v1/groups/{groupId:guid}/members/{userId:guid}/role", async (
            Guid groupId,
            Guid userId,
            UpdateGroupMemberRoleRequest request,
            HttpContext context,
            EnrollmentStore enrollment,
            GroupStore groups,
            CancellationToken cancellationToken) =>
        {
            var principal = await AuthorizationHelpers.GetPrincipalAsync(
                context,
                enrollment,
                cancellationToken);
            if (principal is null)
                return Results.Unauthorized();

            try
            {
                return Results.Ok(await groups.UpdateMemberRoleAsync(
                    principal.UserId,
                    groupId,
                    userId,
                    request,
                    cancellationToken));
            }
            catch (UnauthorizedAccessException)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (InvalidOperationException exception)
            {
                return Results.Conflict(new { error = exception.Message });
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
        });


        // Only the server's durable group membership is trusted here; user
        // subscriptions to SignalR groups are never an access-control boundary.
        app.MapGet("/api/v1/groups/{groupId:guid}/messages", async (
            Guid groupId,
            Guid? afterMessageId,
            int? limit,
            HttpContext context,
            EnrollmentStore enrollment,
            GroupMessageStore messages,
            CancellationToken cancellationToken) =>
        {
            var principal = await AuthorizationHelpers.GetPrincipalAsync(
                context, enrollment, cancellationToken);
            if (principal is null)
                return Results.Unauthorized();

            try
            {
                return Results.Ok(await messages.GetPageAsync(
                    principal.UserId, groupId, afterMessageId,
                    Math.Clamp(limit ?? 50, 1, GroupMessageStore.MaxPageSize),
                    cancellationToken));
            }
            catch (UnauthorizedAccessException)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        });

        app.MapGet("/api/v1/groups/{groupId:guid}/messages/recent", async (
            Guid groupId,
            Guid? beforeMessageId,
            int? limit,
            HttpContext context,
            EnrollmentStore enrollment,
            GroupMessageStore messages,
            CancellationToken cancellationToken) =>
        {
            var principal = await AuthorizationHelpers.GetPrincipalAsync(
                context, enrollment, cancellationToken);
            if (principal is null) return Results.Unauthorized();

            try
            {
                return Results.Ok(await messages.GetRecentPageAsync(
                    principal.UserId, groupId, beforeMessageId,
                    Math.Clamp(limit ?? 50, 1, GroupMessageStore.MaxPageSize),
                    cancellationToken));
            }
            catch (UnauthorizedAccessException)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        });

        app.MapPost("/api/v1/groups/{groupId:guid}/messages", async (
            Guid groupId,
            SendMessageRequest request,
            HttpContext context,
            EnrollmentStore enrollment,
            GroupMessageStore messages,
            GroupMessageDeliveryGate gate,
            IHubContext<ChatHub> hub,
            ILoggerFactory loggers,
            CancellationToken cancellationToken) =>
        {
            var principal = await AuthorizationHelpers.GetPrincipalAsync(
                context, enrollment, cancellationToken);
            if (principal is null)
                return Results.Unauthorized();

            try
            {
                // Serialize send+fan-out against membership removal for this
                // group. The write transaction checks membership again.
                using var dispatchLease = await gate.EnterAsync(groupId, cancellationToken);
                var result = await messages.SendAsync(
                    principal.UserId, groupId, request, cancellationToken);

                if (result.Created)
                {
                    // Membership/disabled state is re-read AFTER persistence.
                    // Never use a cached list or a SignalR group as authority.
                    var recipients = await messages.GetCurrentRecipientsAsync(
                        principal.UserId, groupId, cancellationToken);
                    foreach (var recipient in recipients)
                    {
                        try
                        {
                            await hub.Clients.Group(ChatHub.UserGroup(recipient))
                                .SendAsync("GroupMessageCreated", result.Message,
                                    cancellationToken);
                        }
                        catch (Exception notificationError) when (notificationError is not OperationCanceledException)
                        {
                            loggers.CreateLogger("GroupMessages").LogWarning(
                                notificationError,
                                "Realtime group delivery failed; durable cursor catch-up is required.");
                        }
                    }
                }

                return Results.Ok(result.Message);
            }
            catch (UnauthorizedAccessException)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
            catch (ArgumentException error)
            {
                return Results.BadRequest(new { error = error.Message });
            }
            catch (InvalidOperationException error)
            {
                return Results.Conflict(new { error = error.Message });
            }
        }).RequireRateLimiting("message");

        return app;
    }
}
