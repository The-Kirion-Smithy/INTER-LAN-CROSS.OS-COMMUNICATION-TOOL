using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using InterLan.Contracts;

namespace InterLan.Application;

public sealed class PairedClientConnection(
    ClientPairingState pairingState,
    SessionResponse session,
    HttpClient httpClient) : IDisposable
{
    public ClientPairingState PairingState { get; } = pairingState;
    public SessionResponse Session { get; } = session;
    public HttpClient HttpClient { get; } = httpClient;

    public InterLanApiClient CreateApiClient()
    {
        var client = new InterLanApiClient(HttpClient);
        client.SetBearerToken(Session.BearerToken);
        return client;
    }

    public InterLanRealtimeClient CreateRealtimeClient() =>
        new(PairingState, Session.BearerToken);

    public void Dispose() => HttpClient.Dispose();
}

public sealed class PairedClientSessionManager(
    ClientPairingStateStore? pairingStore = null)
{
    private readonly ClientPairingStateStore _pairingStore =
        pairingStore ?? new ClientPairingStateStore();

    public async Task<PairedClientConnection> LoadAndRenewAsync(
        string pairingStatePath,
        CancellationToken cancellationToken = default)
    {
        var pairing = await _pairingStore.LoadAsync(pairingStatePath, cancellationToken)
            ?? throw new InvalidOperationException("This client is not paired.");

        var client = CreatePinnedHttpClient(pairing);
        try
        {
            using var response = await client.PostAsJsonAsync(
                "/api/v1/auth/device/renew",
                new RenewDeviceSessionRequest(
                    pairing.DeviceId,
                    pairing.DeviceCredential),
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new UnauthorizedAccessException("Paired device is no longer authorized.");

            response.EnsureSuccessStatusCode();

            var session = await response.Content.ReadFromJsonAsync<SessionResponse>(
                cancellationToken: cancellationToken)
                ?? throw new InvalidDataException("Device renewal returned no session.");

            if (session.DeviceId != pairing.DeviceId)
                throw new InvalidDataException("Renewed session does not match the paired device.");

            return new PairedClientConnection(pairing, session, client);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public async Task<PairedClientConnection> RotateCredentialAsync(
        string pairingStatePath,
        SessionResponse currentSession,
        CancellationToken cancellationToken = default)
    {
        var pairing = await _pairingStore.LoadAsync(pairingStatePath, cancellationToken)
            ?? throw new InvalidOperationException("This client is not paired.");

        if (currentSession.DeviceId != pairing.DeviceId ||
            string.IsNullOrWhiteSpace(currentSession.BearerToken))
        {
            throw new UnauthorizedAccessException("The active session does not belong to this paired device.");
        }

        var client = CreatePinnedHttpClient(pairing);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", currentSession.BearerToken);

        try
        {
            using var response = await client.PostAsync(
                $"/api/v1/devices/{pairing.DeviceId:D}/credential/rotate",
                content: null,
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new UnauthorizedAccessException("Device credential rotation was rejected.");

            response.EnsureSuccessStatusCode();

            var rotatedSession = await response.Content.ReadFromJsonAsync<SessionResponse>(
                cancellationToken: cancellationToken)
                ?? throw new InvalidDataException("Credential rotation returned no session.");

            if (rotatedSession.DeviceId != pairing.DeviceId ||
                string.IsNullOrWhiteSpace(rotatedSession.DeviceCredential))
            {
                throw new InvalidDataException("Credential rotation returned an invalid device identity.");
            }

            var rotatedPairing = pairing with
            {
                DeviceCredential = rotatedSession.DeviceCredential
            };

            await _pairingStore.SaveAsync(
                pairingStatePath,
                rotatedPairing,
                cancellationToken);

            return new PairedClientConnection(rotatedPairing, rotatedSession, client);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public async Task UnpairAndForgetAsync(
        string pairingStatePath,
        PairedClientConnection connection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var api = connection.CreateApiClient();
        await api.UnpairDeviceAsync(cancellationToken);
        // Linux Secret Service is asynchronous: never block Avalonia's UI
        // synchronization context while deleting its protected pairing key.
        await _pairingStore.DeleteAsync(pairingStatePath, cancellationToken);
    }

    public static HttpClient CreatePinnedHttpClient(
        ClientPairingState pairing) =>
        PinnedTransportFactory.CreateHttpClient(pairing);
}
