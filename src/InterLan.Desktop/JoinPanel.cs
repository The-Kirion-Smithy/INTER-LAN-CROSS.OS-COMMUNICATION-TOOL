using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using InterLan.Infrastructure;
using InterLan.Application;
using InterLan.Contracts;

namespace InterLan.Desktop;

/// <summary>
/// Human-verified HTTPS trust, one-time invite enrollment, and durable device
/// session renewal. Advertised/discovered fingerprints are never auto-trusted.
/// </summary>
internal sealed class JoinPanel : UserControl, IDisposable
{
    private readonly TextBox _origin = new() { Watermark = "https://192.168.1.20:7443" };
    private readonly ListBox _discovery = new() { Height = 112 };
    private readonly Button _scan = Button("Scan LAN announcements (untrusted)");
    private readonly Button _useAdvertisedAddress = Button("Use selected LAN address (do not trust fingerprint)");
    private readonly TextBox _fingerprint = new() { Watermark = "64-digit SHA-256 from the owner" };
    private readonly TextBox _invite = new() { Watermark = "Owner-issued invitation token" };
    private readonly TextBox _username = new() { Watermark = "Username" };
    private readonly TextBox _displayName = new() { Watermark = "Display name" };
    private readonly TextBox _deviceName = new() { Text = Environment.MachineName };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _verifiedIdentity = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _verify = Button("Verify HTTPS certificate");
    private readonly Button _request = Button("Submit device approval request");
    private readonly Button _exchange = Button("Complete owner-approved pairing");
    private readonly Button _restore = Button("Restore saved device session");
    private readonly Button _unpair = Button("Revoke this device and unpair");
    private readonly Button _logout = Button("Sign out session (keep device pairing)");
    private readonly Button _rotate = Button("Rotate approved device credential");
    private readonly PairedClientSessionManager _sessions = new();
    private readonly ClientPairingStateStore _pairings = new();
    private HttpClient? _provisionalHttp;
    private VerifiedServer? _verified;
    private Guid? _pendingId;
    private string? _pendingSecret;
    private PairedClientConnection? _connection;
    private bool _busy;

    public event Action<PairedClientConnection>? SessionReady;
    public event Action? SessionEnded;

    public JoinPanel()
    {
        var panel = new StackPanel { Margin = new Thickness(18), Spacing = 10 };
        panel.Children.Add(Heading("Join a trusted owner server"));
        panel.Children.Add(new TextBlock
        {
            Text = "Obtain the HTTPS address and SHA-256 certificate fingerprint directly from the owner. A LAN announcement alone is NOT proof of identity.",
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(Heading("LAN discovery — untrusted advertisements"));
        panel.Children.Add(_scan);
        panel.Children.Add(_discovery);
        panel.Children.Add(_useAdvertisedAddress);
        panel.Children.Add(Label("HTTPS server origin"));
        panel.Children.Add(_origin);
        panel.Children.Add(Label("Independently confirmed certificate fingerprint"));
        panel.Children.Add(_fingerprint);
        panel.Children.Add(_verify);
        panel.Children.Add(_verifiedIdentity);
        panel.Children.Add(Heading("Request enrollment"));
        panel.Children.Add(Label("Invitation token (one-time use)"));
        panel.Children.Add(_invite);
        panel.Children.Add(Label("Requested username"));
        panel.Children.Add(_username);
        panel.Children.Add(Label("Display name"));
        panel.Children.Add(_displayName);
        panel.Children.Add(Label("This device's name"));
        panel.Children.Add(_deviceName);
        panel.Children.Add(_request);
        panel.Children.Add(_exchange);
        panel.Children.Add(Heading("Previously approved device"));
        panel.Children.Add(_restore);
        panel.Children.Add(_logout);
        panel.Children.Add(_rotate);
        panel.Children.Add(_unpair);
        panel.Children.Add(_status);
        panel.Children.Add(new TextBlock
        {
            Text = OperatingSystem.IsWindows()
                ? "Windows pairing credentials are encrypted using the existing DPAPI-backed client pairing store."
                : "Pairing is disabled on this OS: the current Linux/macOS pairing store keeps its encryption key alongside the ciphertext. A protected keychain adapter is required before durable pairing is enabled.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75
        });
        Content = new ScrollViewer { Content = panel };

        _scan.Click += async (_, _) => await ExecuteAsync(ScanAsync);
        _useAdvertisedAddress.Click += (_, _) => SelectDiscoveredAddress();
        _verify.Click += async (_, _) => await ExecuteAsync(VerifyAsync);
        _request.Click += async (_, _) => await ExecuteAsync(RequestAsync);
        _exchange.Click += async (_, _) => await ExecuteAsync(ExchangeAsync);
        _restore.Click += async (_, _) => await ExecuteAsync(RestoreAsync);
        _unpair.Click += async (_, _) => await ExecuteAsync(UnpairAsync);
        _logout.Click += async (_, _) => await ExecuteAsync(LogoutAsync);
        _rotate.Click += async (_, _) => await ExecuteAsync(RotateCredentialAsync);

        _status.Text = "No verified server or active paired device. Manual HTTPS enrollment is available on Windows.";
        SetControls();
    }

    private async Task ExecuteAsync(Func<Task> work)
    {
        if (_busy)
            return;
        _busy = true;
        SetControls();
        try { await work(); }
        catch (Exception ex)
        {
            _status.Text = ex switch
            {
                UnauthorizedAccessException => "Operation denied: certificate mismatch, rejected approval or revoked/expired device credentials.",
                HttpRequestException { StatusCode: System.Net.HttpStatusCode.Unauthorized } =>
                    "The server rejected these credentials or the join exchange is not approved.",
                HttpRequestException { StatusCode: System.Net.HttpStatusCode.Forbidden } =>
                    "The server denied this operation.",
                HttpRequestException => "Cannot reach the pinned HTTPS server. Check its address, network and certificate.",
                _ => $"Operation failed: {ex.Message}"
            };
        }
        finally { _busy = false; SetControls(); }
    }

    private async Task ScanAsync()
    {
        var announcements = new Dictionary<string, DiscoveredEntry>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        await using var discovery = new LanDiscoveryClient();
        try
        {
            await foreach (var candidate in discovery.ListenAsync(timeout.Token))
            {
                var address = $"https://{candidate.Remote.Address}:{candidate.Announcement.HttpsPort}";
                announcements[address] = new DiscoveredEntry(
                    candidate.Announcement.ServerName,
                    candidate.Announcement.ServerId,
                    address,
                    candidate.Announcement.CertificateSha256);
                _discovery.ItemsSource = announcements.Values.ToArray();
            }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            // Expected bounded scan completion.
        }
        _status.Text = $"Found {announcements.Count} untrusted LAN advertisements. Confirm certificate identity with the owner separately.";
    }

    private void SelectDiscoveredAddress()
    {
        if (_discovery.SelectedItem is not DiscoveredEntry selected)
        {
            _status.Text = "Select an advertisement before copying its address.";
            return;
        }
        _origin.Text = selected.Origin;
        _fingerprint.Text = ""; // Do not auto-trust the broadcast fingerprint.
        _verifiedIdentity.Text = "";
        _verified = null;
        _pendingId = null;
        _pendingSecret = null;
        _provisionalHttp?.Dispose();
        _provisionalHttp = null;
        _status.Text = "Address copied from untrusted LAN discovery. Ask the owner for the independent fingerprint, enter it manually, and verify.";
        SetControls();
    }

    private async Task VerifyAsync()
    {
        _verified = null;
        _pendingId = null;
        _pendingSecret = null;
        _provisionalHttp?.Dispose();
        _provisionalHttp = null;
        var origin = NativePinnedHttp.NormalizeUri(_origin.Text ?? "");
        var sha = NativePinnedHttp.NormalizeFingerprint(_fingerprint.Text ?? "");
        _provisionalHttp = NativePinnedHttp.Create(origin, sha);
        var info = await NativePinnedHttp.ReadVerifiedInfoAsync(_provisionalHttp, sha);
        if (!info.Server.Configured || info.Server.ServerId is not { } id || id == Guid.Empty)
            throw new InvalidOperationException("This server has not completed owner bootstrap.");
        if (info.HttpsPort != new Uri(origin).Port)
            throw new InvalidOperationException("Server information reports a different HTTPS listener port.");

        _verified = new VerifiedServer(id, origin, sha, info.Server.ServerName ?? "INTER-LAN");
        _verifiedIdentity.Text = $"Confirmed TLS identity: {_verified.Name} ({id:D})\n{origin}\nSHA-256: {sha}";
        _status.Text = "Certificate verified against the independently supplied fingerprint. Request owner approval using an invitation.";
    }

    private async Task RequestAsync()
    {
        RequireWindowsProtection();
        var (server, http) = RequireVerified();
        if (string.IsNullOrWhiteSpace(_invite.Text) ||
            string.IsNullOrWhiteSpace(_username.Text) ||
            string.IsNullOrWhiteSpace(_displayName.Text) ||
            string.IsNullOrWhiteSpace(_deviceName.Text))
            throw new ArgumentException("Invitation, username, display name and device name are required.");
        if (_pendingId is not null)
            throw new InvalidOperationException("An approval request is already outstanding. Reuse it or verify again to discard it.");

        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var request = new SubmitJoinRequest(
            _invite.Text.Trim(),
            _username.Text.Trim(),
            _displayName.Text.Trim(),
            _deviceName.Text.Trim(),
            PlatformName(),
            secret);
        using var response = await http.PostAsJsonAsync("/api/v1/join", request);
        response.EnsureSuccessStatusCode();
        var pending = await response.Content.ReadFromJsonAsync<SubmitJoinResponse>()
            ?? throw new InvalidDataException("Join request response was empty.");
        if (pending.RequestId == Guid.Empty)
            throw new InvalidDataException("Join request ID was invalid.");

        _pendingId = pending.RequestId;
        _pendingSecret = secret;
        _invite.Text = "";
        _status.Text = $"Join request {pending.RequestId:D} is {pending.Status}. Ask the owner to approve it, then select Complete pairing. Keep this application open: the one-time exchange secret is memory-only.";
    }

    private async Task ExchangeAsync()
    {
        RequireWindowsProtection();
        var (server, http) = RequireVerified();
        if (_pendingId is not { } requestId || string.IsNullOrWhiteSpace(_pendingSecret))
            throw new InvalidOperationException("No outstanding enrollment request in this session.");
        using var response = await http.PostAsJsonAsync(
            "/api/v1/join/exchange",
            new ExchangeJoinRequest(requestId, _pendingSecret));

        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new UnauthorizedAccessException("Join not yet approved, rejected, expired or already exchanged.");
        response.EnsureSuccessStatusCode();
        var session = await response.Content.ReadFromJsonAsync<SessionResponse>()
            ?? throw new InvalidDataException("Approved enrollment response was empty.");

        if (session.DeviceId is not { } deviceId || deviceId == Guid.Empty ||
            string.IsNullOrWhiteSpace(session.DeviceCredential) ||
            string.IsNullOrWhiteSpace(session.BearerToken))
        {
            throw new InvalidDataException("Approved enrollment did not return a valid device credential.");
        }

        var pairing = new ClientPairingState(
            server.Id, server.Origin, server.Fingerprint, deviceId,
            session.DeviceCredential, _deviceName.Text!.Trim(), DateTimeOffset.UtcNow);
        var statePath = ClientPairingStateStore.GetDefaultPath();
        // A successful exchange is one-time. Preserve the active bearer even
        // if local storage fails; DO NOT claim the pairing was persisted.
        var established = new PairedClientConnection(pairing, session,
            PinnedTransportFactory.CreateHttpClient(pairing));
        var persisted = false;
        try
        {
            await _pairings.SaveAsync(statePath, pairing);
            persisted = true;
        }
        catch (Exception)
        {
            // Server exchange is already consumed. Keep a usable in-memory
            // bearer session so the device can be explicitly revoked. Do not
            // claim that its credential survived a restart.
            _status.Text = "Approved, but secure local credential storage failed. This session is temporary. Revoke this device before exiting; a fresh invite will be required.";
        }
        finally
        {
            _pendingSecret = null;
            _pendingId = null;
        }

        _connection = established;
        if (persisted)
            _status.Text = "Device paired and locally protected. Session is active.";
        SessionReady?.Invoke(established);
    }

    private async Task RestoreAsync()
    {
        RequireWindowsProtection();
        var path = ClientPairingStateStore.GetDefaultPath();
        var renewed = await _sessions.LoadAndRenewAsync(path);
        try
        {
            var info = await NativePinnedHttp.ReadVerifiedInfoAsync(
                renewed.HttpClient, renewed.PairingState.PinnedCertificateSha256);
            if (info.Server.ServerId != renewed.PairingState.ServerId || !info.Server.Configured)
                throw new UnauthorizedAccessException("Saved server identity differs from the current endpoint.");
            _connection = renewed;
            _origin.Text = renewed.PairingState.ServerUri;
            _fingerprint.Text = renewed.PairingState.PinnedCertificateSha256;
            _verified = new VerifiedServer(
                renewed.PairingState.ServerId,
                renewed.PairingState.ServerUri,
                renewed.PairingState.PinnedCertificateSha256,
                info.Server.ServerName ?? "INTER-LAN");
            _verifiedIdentity.Text = $"Restored {_verified.Name} ({_verified.Id:D}); certificate pin validated.";
            _status.Text = $"Device session restored through secure renewal. Expires {renewed.Session.ExpiresUtc.LocalDateTime:g}.";
            SessionReady?.Invoke(renewed);
        }
        catch
        {
            renewed.Dispose();
            throw;
        }
    }

    private async Task LogoutAsync()
    {
        if (_connection is null)
            throw new InvalidOperationException("No active device session.");
        await _connection.CreateApiClient().LogoutAsync();
        _connection.Dispose();
        _connection = null;
        _status.Text = "Bearer session revoked server-side. Protected device pairing retained for future renewal.";
        SessionEnded?.Invoke();
    }

    private async Task RotateCredentialAsync()
    {
        RequireWindowsProtection();
        if (_connection is null)
            throw new InvalidOperationException("No active device session.");
        var updated = await _sessions.RotateCredentialAsync(
            ClientPairingStateStore.GetDefaultPath(), _connection.Session);
        _connection = updated;
        _status.Text = "Device credential rotated and saved using protected local storage. Previous device sessions invalidated.";
        SessionReady?.Invoke(updated);
    }

    private async Task UnpairAsync()
    {
        if (_connection is null)
            throw new InvalidOperationException("There is no active device session to revoke.");
        // Server revocation must succeed BEFORE local credentials are destroyed.
        await _sessions.UnpairAndForgetAsync(
            ClientPairingStateStore.GetDefaultPath(), _connection);
        _connection.Dispose();
        _connection = null;
        _verified = null;
        _pendingId = null;
        _pendingSecret = null;
        _status.Text = "Device authorization revoked server-side. Local pairing credentials deleted.";
        SessionEnded?.Invoke();
    }

    private (VerifiedServer Server, HttpClient Http) RequireVerified()
    {
        if (_verified is not { } server || _provisionalHttp is null)
            throw new InvalidOperationException("Verify the server certificate first.");
        if (NativePinnedHttp.NormalizeUri(_origin.Text ?? "") != server.Origin ||
            NativePinnedHttp.NormalizeFingerprint(_fingerprint.Text ?? "") != server.Fingerprint)
        {
            throw new UnauthorizedAccessException("Address/fingerprint changed since verification. Verify again.");
        }
        return (server, _provisionalHttp);
    }

    private static string PlatformName() => OperatingSystem.IsWindows() ? "Windows" :
        OperatingSystem.IsMacOS() ? "macOS" : OperatingSystem.IsLinux() ? "Linux" : "Unknown";

    private static void RequireWindowsProtection()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                "Durable pairing is disabled until the current OS has a protected credential key store.");
    }

    private void SetControls()
    {
        _scan.IsEnabled = !_busy;
        _useAdvertisedAddress.IsEnabled = !_busy;
        _verify.IsEnabled = !_busy;
        _request.IsEnabled = !_busy && _verified is not null && _pendingId is null;
        _exchange.IsEnabled = !_busy && _pendingId is not null;
        _restore.IsEnabled = !_busy;
        _unpair.IsEnabled = !_busy && _connection is not null;
        _logout.IsEnabled = !_busy && _connection is not null;
        _rotate.IsEnabled = !_busy && _connection is not null;
    }

    public void Dispose()
    {
        _provisionalHttp?.Dispose();
        _pendingSecret = null;
        _pendingId = null;
        // MainWindow owns/disposes any handed-off active session.
    }

    private sealed record DiscoveredEntry(string Name, Guid Id, string Origin, string AdvertisedFingerprint)
    {
        public override string ToString() =>
            $"{Name} · {Origin} · untrusted advertised ID {Id:D} / SHA-256 {AdvertisedFingerprint}";
    }
    private sealed record VerifiedServer(Guid Id, string Origin, string Fingerprint, string Name);
    private static TextBlock Heading(string text) => new() { Text = text, FontSize = 20, FontWeight = FontWeight.Bold };
    private static TextBlock Label(string text) => new() { Text = text, FontWeight = FontWeight.SemiBold };
    private static Button Button(string text) => new() { Content = text, MinHeight = 38 };
}
