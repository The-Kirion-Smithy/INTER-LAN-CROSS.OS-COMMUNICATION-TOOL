using InterLan.Application;
using InterLan.Infrastructure;
using InterLan.Server;
using Microsoft.Extensions.DependencyInjection;

namespace InterLan.Desktop;

public sealed record LocalOwnerServerSnapshot(
    string DataDirectory,
    string LocalUri,
    string CertificateSha256);

/// <summary>
/// Owns exactly one canonical embedded server. Starting it does not authenticate
/// the desktop user or confer owner privileges.
/// </summary>
public sealed class OwnerServerRuntime : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private InterLanServerInstance? _instance;

    public bool IsRunning => _instance is not null;
    public LocalOwnerServerSnapshot? Snapshot { get; private set; }

    public async Task StartAsync(
        string dataDirectory,
        IReadOnlyDictionary<string, string?>? configurationOverrides = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_instance is not null)
                throw new InvalidOperationException("Owner server is already running.");

            var options = new InterLanServerHostOptions(
                Path.GetFullPath(dataDirectory),
                configurationOverrides);

            var instance = await InterLanServerHost.CreateAsync(
                Array.Empty<string>(),
                options,
                cancellationToken);

            try
            {
                await instance.StartAsync(cancellationToken);

                var settings = instance.Services.GetRequiredService<ServerRuntimeSettings>();
                var certificate = instance.Services.GetRequiredService<ServerCertificateDescriptor>();

                // Loopback bootstrap MUST be reachable for first-time configuration.
                // Do not fall back to issuing bootstrap over an external interface.
                Snapshot = new LocalOwnerServerSnapshot(
                    options.DataDirectory!,
                    $"https://127.0.0.1:{settings.Port}",
                    certificate.Sha256Fingerprint);
                _instance = instance;
            }
            catch
            {
                await instance.DisposeAsync();
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_instance is null)
                return;

            var instance = _instance;
            await instance.DisposeAsync();
            _instance = null;
            Snapshot = null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _gate.Dispose();
    }
}
