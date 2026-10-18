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
    private ChatWorkspace? _chat;
    private GroupWorkspace? _groups;
    private TabItem? _groupsTab;
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
        _chat = new ChatWorkspace(connection);
        _chatTab = new TabItem { Header = "Native messaging", Content = _chat };
        _tabs.Items.Add(_chatTab);
        _groups = new GroupWorkspace(connection);
        _groupsTab = new TabItem { Header = "Groups", Content = _groups };
        _tabs.Items.Add(_groupsTab);
        _tabs.SelectedItem = _chatTab;
        _ = _chat.InitializeAsync();
        _ = _groups.InitializeAsync();
    }

    private void RemoveClientWorkspace()
    {
        if (_groupsTab is not null)
        {
            _tabs.Items.Remove(_groupsTab);
            _groupsTab = null;
            _groups = null;
        }
        if (_chatTab is not null)
        {
            _tabs.Items.Remove(_chatTab);
            _chatTab = null;
            _tabs.SelectedItem = _joinTab;
        }
        if (_chat is not null)
        {
            _ = _chat.DisposeAsync();
            _chat = null;
        }
        _connection?.Dispose();
        _connection = null;
    }
}
