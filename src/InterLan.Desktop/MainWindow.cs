using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace InterLan.Desktop;

/// <summary>
/// First-run owner runtime controls. Account bootstrap and client pairing stay
/// separate operations; starting the owner host does not grant owner identity.
/// </summary>
public sealed class MainWindow : Window
{
    private readonly OwnerServerRuntime _ownerServer = new();
    private readonly TextBlock _status;
    private readonly Button _startServer;
    private readonly Button _stopServer;
    private bool _busy;

    public MainWindow()
    {
        Title = "INTER-LAN";
        Width = 920;
        Height = 620;
        MinWidth = 720;
        MinHeight = 480;

        var title = new TextBlock
        {
            Text = "INTER-LAN",
            FontSize = 34,
            FontWeight = FontWeight.Bold
        };

        var subtitle = new TextBlock
        {
            Text = "Native LAN communication · Owner server controls",
            FontSize = 15,
            Opacity = 0.72
        };

        _status = new TextBlock
        {
            Text = "Start the local owner server, or connect using a separately paired client. The owner account must still be bootstrapped before use.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.8
        };

        _startServer = new Button
        {
            Content = "Start Owner Server",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinHeight = 48
        };
        _startServer.Click += async (_, _) => await StartOwnerServerAsync();

        _stopServer = new Button
        {
            Content = "Stop Owner Server",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinHeight = 48,
            IsEnabled = false
        };
        _stopServer.Click += async (_, _) => await StopOwnerServerAsync();

        var joinServer = new Button
        {
            Content = "Join Server (client pairing UI pending)",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinHeight = 48,
            IsEnabled = false
        };

        var panel = new StackPanel
        {
            Spacing = 16,
            Width = 520,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                title,
                subtitle,
                new Border { Height = 8 },
                _startServer,
                _stopServer,
                joinServer,
                new Border { Height = 8 },
                _status
            }
        };

        Content = new Grid
        {
            Margin = new Thickness(32),
            Children = { panel }
        };

        Closed += async (_, _) =>
        {
            try
            {
                await _ownerServer.DisposeAsync();
            }
            catch
            {
                // Window is already closing: do not expose server secrets
                // or crash the desktop on a best-effort shutdown.
            }
        };
    }

    private static string OwnerDataDirectory()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(local))
            local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");

        return Path.Combine(local, "InterLan", "OwnerServer");
    }

    private async Task StartOwnerServerAsync()
    {
        if (_busy || _ownerServer.IsRunning)
            return;

        SetBusy(true);
        _status.Text = "Starting owner server…";

        try
        {
            var dataDirectory = OwnerDataDirectory();
            Directory.CreateDirectory(dataDirectory);
            await _ownerServer.StartAsync(dataDirectory);
            _status.Text = $"Owner server started. Data directory: {dataDirectory}. Complete one-time owner bootstrap using the local owner setup API before issuing invites.";
        }
        catch (Exception exception)
        {
            _status.Text = $"Could not start owner server: {exception.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task StopOwnerServerAsync()
    {
        if (_busy || !_ownerServer.IsRunning)
            return;

        SetBusy(true);
        _status.Text = "Stopping owner server…";
        try
        {
            await _ownerServer.StopAsync();
            _status.Text = "Owner server stopped. Durable server identity and data were retained.";
        }
        catch (Exception exception)
        {
            _status.Text = $"Could not stop owner server cleanly: {exception.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _startServer.IsEnabled = !busy && !_ownerServer.IsRunning;
        _stopServer.IsEnabled = !busy && _ownerServer.IsRunning;
    }
}
