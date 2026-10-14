using InterLan.Application;
using InterLan.Contracts;
using InterLan.Domain;
using InterLan.Infrastructure;

namespace InterLan.Server;

public static class SystemEndpointMappings
{
    public static IEndpointRouteBuilder MapInterLanSystemEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health", () => Results.Ok(new
        {
            status = "ok",
            apiVersion = ApiContractVersion.Current,
            utc = DateTimeOffset.UtcNow
        }));

        endpoints.MapGet("/api/v1/server/info", async (
            HttpContext context,
            IServerIdentityStore identities,
            ServerRuntimeSettings settings,
            ServerCertificateDescriptor certificate,
            CancellationToken cancellationToken) =>
        {
            // Owner identity and pairing fingerprint must never be served from a cache.
            context.Response.Headers["Cache-Control"] = "no-store";
            var identity = await identities.GetAsync(cancellationToken);

            return Results.Ok(new
            {
                server = new ServerInfoResponse(
                    ApiContractVersion.Current,
                    identity is not null,
                    identity?.ServerId,
                    identity?.ServerName,
                    identity is null ? RuntimeMode.Unconfigured : RuntimeMode.ServerOwner,
                    NativeDesktopSupported: true,
                    WebClientSupported: true),
                httpsPort = settings.Port,
                certificateSha256 = certificate.Sha256Fingerprint
            });
        });

        endpoints.MapPut("/api/v1/server/settings", async (
            HttpContext context,
            UpdateServerSettingsRequest request,
            EnrollmentStore enrollment,
            ServerRuntimeSettings activeSettings,
            ServerSettingsStore settingsStore,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await AuthorizationHelpers.RequireOwnerAsync(
                    context,
                    enrollment,
                    cancellationToken);

                if (string.IsNullOrWhiteSpace(request.StoragePath))
                    return Results.BadRequest(new { error = "StoragePath is required." });

                var persisted = new ServerRuntimeSettings(
                    request.BindAddress,
                    request.Port,
                    request.DiscoveryEnabled,
                    request.ClientApprovalRequired,
                    Path.GetFullPath(request.StoragePath.Trim()));

                await settingsStore.SaveAsync(persisted, cancellationToken);

                return Results.Ok(new ServerSettingsSnapshotResponse(
                    ToResponse(activeSettings),
                    ToResponse(persisted),
                    activeSettings != persisted));
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Unauthorized();
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
        });

        endpoints.MapGet("/api/v1/server/settings", async (
            HttpContext context,
            EnrollmentStore enrollment,
            ServerRuntimeSettings activeSettings,
            ServerSettingsStore settingsStore,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await AuthorizationHelpers.RequireOwnerAsync(
                    context,
                    enrollment,
                    cancellationToken);

                var persisted = await settingsStore.LoadPersistedAsync(cancellationToken)
                    ?? activeSettings;

                return Results.Ok(new ServerSettingsSnapshotResponse(
                    ToResponse(activeSettings),
                    ToResponse(persisted),
                    activeSettings != persisted));
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Unauthorized();
            }
        });

        return endpoints;
    }

    private static ServerSettingsResponse ToResponse(ServerRuntimeSettings settings) =>
        new(
            settings.BindAddress,
            settings.Port,
            settings.DiscoveryEnabled,
            settings.ClientApprovalRequired,
            settings.StoragePath);
}
