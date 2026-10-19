using InterLan.Infrastructure;

namespace InterLan.Server;

public static class AttachmentEndpointMappings
{
    public static IEndpointRouteBuilder MapInterLanAttachmentEndpoints(
        this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/attachments/policy", async (
            HttpContext context, EnrollmentStore enrollment,
            AttachmentStore store, CancellationToken cancellationToken) =>
        {
            var principal = await AuthorizationHelpers.GetPrincipalAsync(
                context, enrollment, cancellationToken);
            return principal is null
                ? Results.Unauthorized()
                : Results.Ok(store.Policy());
        });

        app.MapGet("/api/v1/groups/{groupId:guid}/attachments", async (
            Guid groupId, Guid? beforeAttachmentId, int? limit,
            HttpContext context, EnrollmentStore enrollment,
            AttachmentStore store, CancellationToken cancellationToken) =>
            await ListScopedAsync(context, enrollment, store,
                "GROUP", groupId, beforeAttachmentId, limit, cancellationToken));

        app.MapGet("/api/v1/direct/{conversationId:guid}/attachments", async (
            Guid conversationId, Guid? beforeAttachmentId, int? limit,
            HttpContext context, EnrollmentStore enrollment,
            AttachmentStore store, CancellationToken cancellationToken) =>
            await ListScopedAsync(context, enrollment, store,
                "DIRECT", conversationId, beforeAttachmentId, limit, cancellationToken));

        app.MapPost("/api/v1/groups/{groupId:guid}/attachments", async (
            Guid groupId, HttpContext context,
            EnrollmentStore enrollment, AttachmentStore store,
            CancellationToken cancellationToken) =>
            await UploadAsync(context, enrollment, store,
                "GROUP", groupId, cancellationToken))
            .RequireRateLimiting("message");

        app.MapPost("/api/v1/direct/{conversationId:guid}/attachments", async (
            Guid conversationId, HttpContext context,
            EnrollmentStore enrollment, AttachmentStore store,
            CancellationToken cancellationToken) =>
            await UploadAsync(context, enrollment, store,
                "DIRECT", conversationId, cancellationToken))
            .RequireRateLimiting("message");

        app.MapGet("/api/v1/attachments/{attachmentId:guid}/metadata", async (
            Guid attachmentId, HttpContext context,
            EnrollmentStore enrollment, AttachmentStore store,
            CancellationToken cancellationToken) =>
        {
            var principal = await AuthorizationHelpers.GetPrincipalAsync(
                context, enrollment, cancellationToken);
            if (principal is null) return Results.Unauthorized();
            // Scoped attachment metadata is not public cacheable content.
            context.Response.Headers["Cache-Control"] = "no-store";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            try
            {
                return Results.Ok(await store.GetAuthorizedAsync(
                    principal.UserId, attachmentId, cancellationToken));
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

        app.MapGet("/api/v1/attachments/{attachmentId:guid}/content", async (
            Guid attachmentId, HttpContext context,
            EnrollmentStore enrollment, AttachmentStore store,
            CancellationToken cancellationToken) =>
        {
            var principal = await AuthorizationHelpers.GetPrincipalAsync(
                context, enrollment, cancellationToken);
            if (principal is null) return Results.Unauthorized();
            // Never allow shared caches or content sniffing for private blobs.
            context.Response.Headers["Cache-Control"] = "no-store";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            try
            {
                var download = await store.OpenReadAsync(
                    principal.UserId, attachmentId, cancellationToken);
                // Never inline untrusted MIME or user-supplied markup. Force
                // an octet-stream attachment disposition to the browser.
                return Results.File(download.Content,
                    contentType: "application/octet-stream",
                    fileDownloadName: download.Metadata.OriginalFilename,
                    enableRangeProcessing: false);
            }
            catch (UnauthorizedAccessException)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (FileNotFoundException)
            {
                return Results.NotFound();
            }
        });

        return app;
    }

    private static async Task<IResult> ListScopedAsync(
        HttpContext context, EnrollmentStore enrollment,
        AttachmentStore store, string scope, Guid scopeId,
        Guid? beforeAttachmentId, int? limit, CancellationToken cancellationToken)
    {
        var principal = await AuthorizationHelpers.GetPrincipalAsync(
            context, enrollment, cancellationToken);
        if (principal is null) return Results.Unauthorized();

        try
        {
            return Results.Ok(await store.ListAuthorizedAsync(
                principal.UserId, scope, scopeId, beforeAttachmentId,
                Math.Clamp(limit ?? 50, 1, 100), cancellationToken));
        }
        catch (UnauthorizedAccessException)
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
    }

    private static async Task<IResult> UploadAsync(
        HttpContext context, EnrollmentStore enrollment,
        AttachmentStore store, string scope, Guid scopeId,
        CancellationToken cancellationToken)
    {
        var principal = await AuthorizationHelpers.GetPrincipalAsync(
            context, enrollment, cancellationToken);
        if (principal is null) return Results.Unauthorized();

        if (context.Request.ContentLength is { } size &&
            size > AttachmentStore.MaxUploadBytes)
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

        try
        {
            var metadata = await store.SaveAsync(
                principal.UserId, scope, scopeId,
                context.Request.Body,
                context.Request.Headers["X-InterLan-Filename"].ToString(),
                context.Request.ContentType,
                cancellationToken);
            return Results.Created(
                $"/api/v1/attachments/{metadata.AttachmentId:D}/metadata",
                metadata);
        }
        catch (UnauthorizedAccessException)
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }
        catch (AttachmentPayloadTooLargeException)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }
        catch (ArgumentException error)
        {
            return Results.BadRequest(new { error = error.Message });
        }
        catch (InvalidOperationException error)
        {
            return Results.Conflict(new { error = error.Message });
        }
    }
}
