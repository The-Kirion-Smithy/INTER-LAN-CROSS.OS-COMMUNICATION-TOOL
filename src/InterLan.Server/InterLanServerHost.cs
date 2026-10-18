using System.Net;
using InterLan.Application;
using InterLan.Infrastructure;
using InterLan.Server.Networking;
using InterLan.Server.Realtime;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

namespace InterLan.Server;

public static class InterLanServerHost
{
    public static async Task<InterLanServerInstance> CreateAsync(
        string[] args,
        InterLanServerHostOptions hostOptions,
        CancellationToken cancellationToken = default)
    {
        var application = await BuildAsync(args, hostOptions, cancellationToken);
        return new InterLanServerInstance(application);
    }

    public static Task<WebApplication> BuildAsync(
        string[] args,
        CancellationToken cancellationToken = default) =>
        BuildAsync(args, new InterLanServerHostOptions(), cancellationToken);

    public static async Task<WebApplication> BuildAsync(
        string[] args,
        InterLanServerHostOptions hostOptions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(hostOptions);

        var builder = WebApplication.CreateBuilder(args);

        if (hostOptions.ConfigurationOverrides is { Count: > 0 })
            builder.Configuration.AddInMemoryCollection(hostOptions.ConfigurationOverrides);

        // Browser SignalR transports may need the standard access_token query parameter.
        // Suppress framework request-start logging so bearer material is not written to logs.
        builder.Logging.AddFilter(
            "Microsoft.AspNetCore.Hosting.Diagnostics",
            LogLevel.Warning);

        var dataDirectory = hostOptions.DataDirectory;
        if (string.IsNullOrWhiteSpace(dataDirectory))
            dataDirectory = Environment.GetEnvironmentVariable("INTERLAN_DATA_DIR");
        if (string.IsNullOrWhiteSpace(dataDirectory))
            dataDirectory = Path.Combine(AppContext.BaseDirectory, "data");

        dataDirectory = Path.GetFullPath(dataDirectory);

        var database = new SqliteDatabase(Path.Combine(dataDirectory, "interlan.db"));
        await database.InitializeAsync(cancellationToken);

        var persistedSettings = await new ServerSettingsStore(database)
            .LoadAsync(dataDirectory, cancellationToken);

        var runtimeSettings = ServerRuntimeSettingsResolver.ApplyExplicitOverrides(
            persistedSettings,
            builder.Configuration);

        if (!IPAddress.TryParse(runtimeSettings.BindAddress, out var bindAddress))
        {
            throw new InvalidOperationException(
                $"Resolved server bind address is invalid: {runtimeSettings.BindAddress}");
        }

        var certificate = ServerCertificateManager.LoadOrCreate(dataDirectory);

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(
                bindAddress,
                runtimeSettings.Port,
                listen => listen.UseHttps(certificate.Certificate));
        });

        builder.Services.AddSingleton(database);
        builder.Services.AddSingleton(runtimeSettings);
        builder.Services.AddSingleton<ServerSettingsStore>();
        builder.Services.AddSingleton(certificate);
        builder.Services.AddSingleton<IServerIdentityStore, SqliteServerIdentityStore>();
        builder.Services.AddSingleton<EnrollmentStore>();
        builder.Services.AddSingleton<ChatStore>();
        builder.Services.AddSingleton<GroupStore>();
        builder.Services.AddSingleton<RealtimeConnectionRegistry>();
        builder.Services.AddSingleton<RealtimeTicketStore>();
        builder.Services.AddHostedService<LanDiscoveryBroadcaster>();
        builder.Services.AddSignalR();
        builder.Services.AddProblemDetails();
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy("auth", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    RateLimitPartitionKeys.RemoteAddress(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 8,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));

            options.AddPolicy("join", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    RateLimitPartitionKeys.RemoteAddress(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 20,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));

            options.AddPolicy("message", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    RateLimitPartitionKeys.AuthenticatedClient(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 120,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));
        });

        var app = builder.Build();

        app.UseExceptionHandler();
        app.UseRateLimiter();
        app.UseDefaultFiles();
        app.UseStaticFiles();

        app.MapInterLanSystemEndpoints();
        app.MapInterLanEnrollmentEndpoints();
        app.MapInterLanMessagingEndpoints();
        app.MapInterLanGroupEndpoints();

        app.MapHub<ControlHub>("/hubs/control");
        app.MapHub<ChatHub>("/hubs/chat");
        app.MapFallbackToFile("/client/{*path:nonfile}", "client/index.html");

        return app;
    }
}
