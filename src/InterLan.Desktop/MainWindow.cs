using Avalonia;
using Avalonia.Controls;
using InterLan.Application;

namespace InterLan.Desktop;

/// <summary>Native composition for one embedded owner host and one paired client session.</summary>
public sealed class MainWindow : Window
{
    private readonly OwnerServerRuntime _ownerRuntime = new();
    private readonly OwnerPanel _ownerPanel;
    private readonly JoinPanel _joinPanel;
    private readonly TabControl _tabs = new();
    private readonly TabItem _ownerTab;
    private readonly TabItem _joinTab;
    private PairedClientConnection? _connection;
    private TabItem? _chatTab;

    public MainWindow()
    {
        Title = "INTER-LAN · Native";
        Width = 1080;
        Height = 760;
        MinWidth = 650;
        MinHeight = 480;

        _ownerPanel = new OwnerPanel(_ownerRuntime);
        _joinPanel = new JoinPanel();
        _ownerTab = new TabItem { Header = "Owner server", Content = _ownerPanel };
        _joinTab = new TabItem { Header = "Join trusted server", Content = _joinPanel };
        _tabs.Items.Add(_ownerTab);
        _tabs.Items.Add(_joinTab);
        _tabs.Margin = new Thickness(12);
        Content = _tabs;

        _joinPanel.SessionReady += OnSessionReady;
        _joinPanel.SessionEnded += RemoveClientWorkspace;
        Closed += async (_, _) =>
        {
            RemoveClientWorkspace();
            _joinPanel.Dispose();
            _ownerPanel.Dispose();
            try { await _ownerRuntime.DisposeAsync(); }
            catch { /* Best effort: application is closing. */ }
        };
    }

    private void OnSessionReady(PairedClientConnection connection)
    {
        RemoveClientWorkspace();
        _connection = connection;
        _chatTab = new TabItem
        {
            Header = "Native messaging",
            Content = new TextBlock
            {
                Text = "Device session established. Conversation workspace integration follows in this development branch."
            }
        };
        _tabs.Items.Add(_chatTab);
        _tabs.SelectedItem = _chatTab;
    }

    private void RemoveClientWorkspace()
    {
        if (_chatTab is not null)
        {
            _tabs.Items.Remove(_chatTab);
            _chatTab = null;
            _tabs.SelectedItem = _joinTab;
        }
        _connection?.Dispose();
        _connection = null;
    }
}
