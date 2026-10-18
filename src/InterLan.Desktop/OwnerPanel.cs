using System.Net.Http.Headers;
using System.Net.Http.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using InterLan.Contracts;

namespace InterLan.Desktop;

/// <summary>
/// Real loopback-only first-run owner setup and authenticated owner management.
/// Passwords and bearer tokens are held only in memory for this window lifetime.
/// </summary>
internal sealed class OwnerPanel : UserControl, IDisposable
{
    private readonly OwnerServerRuntime _runtime;
    private readonly TextBox _directory = new() { Text = GetDefaultDirectory() };
    private readonly TextBox _serverName = new() { Text = "My INTER-LAN Server" };
    private readonly TextBox _username = new() { Text = "owner" };
    private readonly TextBox _displayName = new() { Text = "Server Owner" };
    private readonly TextBox _password = new() { PasswordChar = '●', Watermark = "At least 12 characters" };
    private readonly TextBox _confirmation = new() { PasswordChar = '●', Watermark = "Repeat password" };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _identity = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _invite = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ListBox _pending = new() { Height = 144 };
    private readonly ListBox _devices = new() { Height = 124 };
    private readonly Button _start = Button("Start owner server");
    private readonly Button _stop = Button("Stop server");
    private readonly Button _bootstrap = Button("Create the owner account");
    private readonly Button _login = Button("Sign in as owner");
    private readonly Button _issueInvite = Button("Issue 60-minute invitation");
    private readonly Button _refresh = Button("Refresh pending requests and devices");
    private readonly Button _approve = Button("Approve selected request");
    private readonly Button _reject = Button("Reject selected request");
    private readonly Button _revokeDevice = Button("Revoke selected device");
    private HttpClient? _http;
    private bool _configured;
    private bool _authenticated;
    private bool _busy;

    public OwnerPanel(OwnerServerRuntime runtime)
    {
        _runtime = runtime;
        var stack = new StackPanel { Spacing = 10, Margin = new Thickness(18) };
        stack.Children.Add(Heading("Create or manage a local owner server"));
        stack.Children.Add(new TextBlock {
            Text = "The embedded server is the same canonical HTTPS server used by browser and native clients. Starting it does not grant owner rights.",
            TextWrapping = TextWrapping.Wrap });
        stack.Children.Add(Label("Durable server data directory"));
        stack.Children.Add(_directory);
        stack.Children.Add(Row(_start, _stop));
        stack.Children.Add(_status);
        stack.Children.Add(_identity);
        stack.Children.Add(Heading("First-time setup / owner sign-in"));
        stack.Children.Add(Label("Server display name"));
        stack.Children.Add(_serverName);
        stack.Children.Add(Label("Owner username"));
        stack.Children.Add(_username);
        stack.Children.Add(Label("Owner display name"));
        stack.Children.Add(_displayName);
        stack.Children.Add(Label("Owner password (never stored by desktop)"));
        stack.Children.Add(_password);
        stack.Children.Add(Label("Confirm password (first setup only)"));
        stack.Children.Add(_confirmation);
        stack.Children.Add(Row(_bootstrap, _login));
        stack.Children.Add(Heading("Authenticated owner operations"));
        stack.Children.Add(_issueInvite);
        stack.Children.Add(_invite);
        stack.Children.Add(_refresh);
        stack.Children.Add(Label("Pending device join requests"));
        stack.Children.Add(_pending);
        stack.Children.Add(Row(_approve, _reject));
        stack.Children.Add(Label("Registered devices (including revoked devices)"));
        stack.Children.Add(_devices);
        stack.Children.Add(_revokeDevice);

        Content = new ScrollViewer { Content = stack };
        _start.Click += async (_, _) => await ExecuteAsync(StartAsync);
        _stop.Click += async (_, _) => await ExecuteAsync(StopAsync);
        _bootstrap.Click += async (_, _) => await ExecuteAsync(BootstrapAsync);
        _login.Click += async (_, _) => await ExecuteAsync(LoginAsync);
        _issueInvite.Click += async (_, _) => await ExecuteAsync(InviteAsync);
        _refresh.Click += async (_, _) => await ExecuteAsync(RefreshAsync);
        _approve.Click += async (_, _) => await ExecuteAsync(() => DecideAsync(true));
        _reject.Click += async (_, _) => await ExecuteAsync(() => DecideAsync(false));
        _revokeDevice.Click += async (_, _) => await ExecuteAsync(RevokeDeviceAsync);
        SetControls();
        _status.Text = "Stopped. The existing data directory is reused after restart.";
    }

    private static string GetDefaultDirectory()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root))
            root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        return Path.Combine(root, "InterLan", "OwnerServer");
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
            // Never include request bodies, passwords, tokens or raw exception ToString.
            _status.Text = ex switch
            {
                HttpRequestException => "HTTPS request failed. Check the server address, certificate and network.",
                UnauthorizedAccessException => "Authentication, authorization or certificate pinning rejected this operation.",
                _ => $"Operation failed: {ex.Message}"
            };
        }
        finally
        {
            _busy = false;
            SetControls();
        }
    }

    private async Task StartAsync()
    {
        if (string.IsNullOrWhiteSpace(_directory.Text))
            throw new ArgumentException("Choose a data directory.");
        await _runtime.StartAsync(_directory.Text);
        try
        {
            var snapshot = _runtime.Snapshot ??
                throw new InvalidOperationException("Server did not expose its running endpoint.");
            _http = NativePinnedHttp.Create(snapshot.LocalUri, snapshot.CertificateSha256);
            var info = await NativePinnedHttp.ReadVerifiedInfoAsync(_http, snapshot.CertificateSha256);
            _configured = info.Server.Configured;
            _authenticated = false;
            _identity.Text =
                $"Local HTTPS: {snapshot.LocalUri}\nCertificate SHA-256: {snapshot.CertificateSha256}\n" +
                $"Server: {info.Server.ServerName ?? "(not configured)"}\nData: {snapshot.DataDirectory}";
            _status.Text = _configured
                ? "Server running and configured. Sign in using the durable owner account."
                : "Server running but unconfigured. Complete the one-time local owner bootstrap below.";
        }
        catch
        {
            DisposeClient();
            await _runtime.StopAsync();
            throw;
        }
    }

    private async Task StopAsync()
    {
        DisposeClient();
        _authenticated = false;
        _configured = false;
        _password.Text = "";
        _confirmation.Text = "";
        await _runtime.StopAsync();
        _identity.Text = "";
        _invite.Text = "";
        _pending.ItemsSource = null;
        _devices.ItemsSource = null;
        _status.Text = "Server stopped. Its SQLite database and TLS certificate are retained in the data directory.";
    }

    private async Task BootstrapAsync()
    {
        if (_configured || _http is null || !_runtime.IsRunning)
            throw new InvalidOperationException("First-time bootstrap is available only on a running unconfigured local server.");
        if ((_password.Text ?? "").Length < 12 || _password.Text != _confirmation.Text)
            throw new ArgumentException("Use a password of at least 12 characters and match both password fields.");
        if (string.IsNullOrWhiteSpace(_serverName.Text) ||
            string.IsNullOrWhiteSpace(_username.Text) ||
            string.IsNullOrWhiteSpace(_displayName.Text))
            throw new ArgumentException("Server name, username and display name are required.");

        using var response = await _http.PostAsJsonAsync(
            "/api/v1/bootstrap/server",
            new BootstrapServerRequest(
                _serverName.Text.Trim(),
                _username.Text.Trim(),
                _displayName.Text.Trim(),
                _password.Text));
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<BootstrapServerResponse>()
            ?? throw new InvalidDataException("Owner bootstrap response was empty.");
        if (!string.Equals(created.CertificateSha256, _runtime.Snapshot!.CertificateSha256,
                StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Bootstrap reported a different server certificate.");

        _configured = true;
        _confirmation.Text = "";
        _status.Text = "Owner identity created on the canonical server. Sign in to manage invitations and devices.";
        await LoginAsync();
    }

    private async Task LoginAsync()
    {
        if (!_configured || _http is null)
            throw new InvalidOperationException("Start and configure the server before signing in.");
        if (string.IsNullOrWhiteSpace(_username.Text) || string.IsNullOrWhiteSpace(_password.Text))
            throw new ArgumentException("Enter the owner username and password.");

        _http.DefaultRequestHeaders.Authorization = null;
        using var response = await _http.PostAsJsonAsync(
            "/api/v1/auth/owner/login",
            new LoginRequest(_username.Text.Trim(), _password.Text));
        _password.Text = "";
        _confirmation.Text = "";
        response.EnsureSuccessStatusCode();
        var session = await response.Content.ReadFromJsonAsync<SessionResponse>()
            ?? throw new InvalidDataException("Owner session response was empty.");
        if (!string.Equals(session.Role, "Owner", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(session.BearerToken))
            throw new UnauthorizedAccessException("Server did not grant an owner session.");
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", session.BearerToken);
        _authenticated = true;
        _status.Text = $"Authenticated owner session, expires {session.ExpiresUtc.LocalDateTime:g}.";
        await RefreshAsync();
    }

    private async Task InviteAsync()
    {
        var http = RequireOwnerClient();
        using var response = await http.PostAsJsonAsync(
            "/api/v1/invites", new CreateInviteRequest(60));
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<InviteResponse>()
            ?? throw new InvalidDataException("Invitation response was empty.");
        _invite.Text = $"Invitation token (share privately): {result.InviteToken}\nExpires: {result.ExpiresUtc.LocalDateTime:g}";
    }

    private async Task RefreshAsync()
    {
        var http = RequireOwnerClient();
        var pending = await http.GetFromJsonAsync<PendingJoinRequestResponse[]>("/api/v1/join/pending")
            ?? [];
        var devices = await http.GetFromJsonAsync<DeviceSummaryResponse[]>("/api/v1/devices")
            ?? [];
        _pending.ItemsSource = pending.Select(x => new PendingEntry(x)).ToArray();
        _devices.ItemsSource = devices.Select(x => new DeviceEntry(x)).ToArray();
        _status.Text = $"Owner authenticated. {pending.Length} pending requests, {devices.Length} devices.";
    }

    private async Task DecideAsync(bool approve)
    {
        if (_pending.SelectedItem is not PendingEntry chosen)
            throw new InvalidOperationException("Select a pending join request.");
        var http = RequireOwnerClient();
        using var response = await http.PostAsJsonAsync(
            $"/api/v1/join/{chosen.Value.RequestId:D}/decision",
            new JoinDecisionRequest(approve));
        response.EnsureSuccessStatusCode();
        await RefreshAsync();
    }

    private async Task RevokeDeviceAsync()
    {
        if (_devices.SelectedItem is not DeviceEntry selected)
            throw new InvalidOperationException("Select a device.");
        if (selected.Value.RevokedUtc is not null)
            throw new InvalidOperationException("That device has already been revoked.");
        var http = RequireOwnerClient();
        using var response = await http.PostAsync(
            $"/api/v1/devices/{selected.Value.DeviceId:D}/revoke", null);
        response.EnsureSuccessStatusCode();
        await RefreshAsync();
    }

    private HttpClient RequireOwnerClient() =>
        _authenticated && _http is not null
            ? _http
            : throw new UnauthorizedAccessException("Sign in as owner first.");

    private void DisposeClient()
    {
        _http?.DefaultRequestHeaders.Authorization = null;
        _http?.Dispose();
        _http = null;
    }

    private void SetControls()
    {
        _start.IsEnabled = !_busy && !_runtime.IsRunning;
        _stop.IsEnabled = !_busy && _runtime.IsRunning;
        _directory.IsEnabled = !_busy && !_runtime.IsRunning;
        _bootstrap.IsEnabled = !_busy && _runtime.IsRunning && !_configured;
        _login.IsEnabled = !_busy && _configured;
        foreach (var button in new[] { _issueInvite, _refresh, _approve, _reject, _revokeDevice })
            button.IsEnabled = !_busy && _authenticated;
    }

    public void Dispose()
    {
        DisposeClient();
        _password.Text = "";
        _confirmation.Text = "";
    }

    private sealed record PendingEntry(PendingJoinRequestResponse Value)
    {
        public override string ToString() =>
            $"{Value.DisplayName} (@{Value.Username}) · {Value.DeviceName} [{Value.Platform}] · {Value.RequestId}";
    }
    private sealed record DeviceEntry(DeviceSummaryResponse Value)
    {
        public override string ToString() =>
            $"{Value.DisplayName} · {Value.DeviceName} ({Value.Platform}) · {(Value.RevokedUtc is null ? "approved" : "revoked")}";
    }
    private static Button Button(string text) => new() { Content = text, MinHeight = 36 };
    private static TextBlock Label(string text) => new() { Text = text, FontWeight = FontWeight.SemiBold };
    private static TextBlock Heading(string text) => new() { Text = text, FontSize = 20, FontWeight = FontWeight.Bold };
    private static StackPanel Row(params Control[] controls)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        foreach (var control in controls) panel.Children.Add(control);
        return panel;
    }
}
