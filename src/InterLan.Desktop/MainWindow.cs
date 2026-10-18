using Avalonia;
using Avalonia.Controls;

namespace InterLan.Desktop;

/// <summary>The shell owns the canonical server and closes it with the window.</summary>
public sealed class MainWindow : Window
{
    private readonly OwnerServerRuntime _ownerRuntime = new();
    private readonly OwnerPanel _ownerPanel;

    public MainWindow()
    {
        Title = "INTER-LAN · Native";
        Width = 1000;
        Height = 740;
        MinWidth = 650;
        MinHeight = 480;
        _ownerPanel = new OwnerPanel(_ownerRuntime);
        Content = new TabControl
        {
            Margin = new Thickness(12),
            Items =
            {
                new TabItem { Header = "Owner server", Content = _ownerPanel }
            }
        };
        Closed += async (_, _) =>
        {
            _ownerPanel.Dispose();
            try { await _ownerRuntime.DisposeAsync(); }
            catch { /* Best effort: the window is already closing. */ }
        };
    }
}
