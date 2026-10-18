using System.Net.Http.Json;
using InterLan.Application;
using InterLan.Contracts;

namespace InterLan.Desktop;

internal sealed record NativeServerInfoEnvelope(
    ServerInfoResponse Server,
    int HttpsPort,
    string CertificateSha256);

/// <summary>
/// Only accepts a fingerprint established independently by the human (joining)
/// or the in-process certificate descriptor (owning). The server's advertised
/// fingerprint is never itself a trust decision.
/// </summary>
internal static class NativePinnedHttp
{
    public static string NormalizeUri(string raw)
    {
        if (!Uri.TryCreate(raw?.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            uri.AbsolutePath != "/")
        {
            throw new ArgumentException("Use an HTTPS server origin, such as https://192.168.1.20:7443.");
        }

        return uri.GetLeftPart(UriPartial.Authority);
    }

    public static string NormalizeFingerprint(string raw)
    {
        var canonical = (raw ?? "").Replace(":", "", StringComparison.Ordinal)
            .Replace("-", "", StringComparison.Ordinal)
            .Replace(" ", "", StringComparison.Ordinal)
            .Trim();

        if (canonical.Length != 64 || !canonical.All(Uri.IsHexDigit))
            throw new ArgumentException("A 64-digit SHA-256 certificate fingerprint is required.");

        return canonical.ToLowerInvariant();
    }

    public static HttpClient Create(string serverUri, string fingerprint)
    {
        var uri = NormalizeUri(serverUri);
        var sha = NormalizeFingerprint(fingerprint);

        // Ephemeral transport descriptor; it is NEVER saved as a paired identity.
        // PinnedTransportFactory only reads the certificate hash from this value.
        var transportOnly = new ClientPairingState(
            Guid.Empty, uri, sha, Guid.Empty, "", "Unpaired TLS probe",
            DateTimeOffset.UtcNow);

        return new HttpClient(PinnedTransportFactory.CreateHandler(transportOnly))
        {
            BaseAddress = new Uri(uri),
            Timeout = TimeSpan.FromSeconds(15)
        };
    }

    public static async Task<NativeServerInfoEnvelope> ReadVerifiedInfoAsync(
        HttpClient client,
        string expectedSha256,
        CancellationToken cancellationToken = default)
    {
        var envelope = await client.GetFromJsonAsync<NativeServerInfoEnvelope>(
            "/api/v1/server/info", cancellationToken)
            ?? throw new InvalidDataException("Server information was empty.");

        if (!string.Equals(
            NormalizeFingerprint(envelope.CertificateSha256),
            NormalizeFingerprint(expectedSha256),
            StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("Server certificate identity differs from the pinned fingerprint.");
        }

        return envelope;
    }
}
