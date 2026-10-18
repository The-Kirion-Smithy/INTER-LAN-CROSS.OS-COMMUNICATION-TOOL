using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using InterLan.Application;
using InterLan.Contracts;

namespace InterLan.Desktop;

/// <summary>
/// Real authenticated direct-message workspace. Messages only appear as
/// persisted after an HTTP response or a canonical SignalR notification.
/// </summary>
internal sealed class ChatWorkspace : UserControl, IAsyncDisposable
{
    private readonly PairedClientConnection _connection;
    private readonly InterLanApiClient _api;
    private readonly InterLanRealtimeClient _realtime;
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _selected = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ListBox _inbox = new() { Height = 260 };
    private readonly ListBox _directory = new() { Height = 190 };
    private readonly ListBox _messages = new() { MinHeight = 260 };
    private readonly TextBox _composer = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 78, Watermark = "Write a direct message" };
    private readonly Button _send = Button("Send message");
    private readonly Button _older = Button("Load earlier messages");
    private readonly Button _refresh = Button("Refresh inbox / directory");
    private readonly Button _open = Button("Open direct message with selected person");
    private readonly Button _reloadThread = Button("Refresh selected conversation");
    private readonly List<MessageResponse> _history = [];
    private Guid? _conversationId;
    private Guid? _olderBeforeMessageId;
    private string? _retryBody;
    private Guid? _retryClientMessageId;
    private bool _busy;
    private bool _disposed;

    public ChatWorkspace(PairedClientConnection connection)
    {
        _connection = connection;
        _api = connection.CreateApiClient();
        _realtime = connection.CreateRealtimeClient();

        var left = new StackPanel { Spacing = 10, Margin = new Thickness(8) };
        left.Children.Add(Heading("Conversations"));
        left.Children.Add(_refresh);
        left.Children.Add(_inbox);
        left.Children.Add(Heading("People on this server"));
        left.Children.Add(_directory);
        left.Children.Add(_open);

        var right = new StackPanel { Spacing = 10, Margin = new Thickness(8) };
        right.Children.Add(Heading("Direct messages"));
        right.Children.Add(_selected);
        right.Children.Add(_reloadThread);
        right.Children.Add(_older);
        right.Children.Add(_messages);
        right.Children.Add(_composer);
        right.Children.Add(_send);
        right.Children.Add(_status);

        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("295,*") };
        var leftScroll = new ScrollViewer { Content = left };
        var rightScroll = new ScrollViewer { Content = right };
        Grid.SetColumn(leftScroll, 0);
        Grid.SetColumn(rightScroll, 1);
        columns.Children.Add(leftScroll);
        columns.Children.Add(rightScroll);
        Content = columns;

        _refresh.Click += async (_, _) => await ExecuteAsync(RefreshListsAsync);
        _reloadThread.Click += async (_, _) => await ExecuteAsync(RefreshThreadAsync);
        _older.Click += async (_, _) => await ExecuteAsync(LoadOlderAsync);
        _open.Click += async (_, _) => await ExecuteAsync(OpenSelectedUserAsync);
        _send.Click += async (_, _) => await ExecuteAsync(SendAsync);
        _inbox.SelectionChanged += async (_, _) =>
        {
            if (_inbox.SelectedItem is InboxEntry entry)
                await ExecuteAsync(() => SelectConversationAsync(
                    entry.Value.ConversationId, entry.Value.OtherUser.DisplayName));
        };

        _realtime.MessageCreated += OnMessage;
        _realtime.MessageEdited += OnMessage;
        _realtime.MessageDeleted += OnDelete;
        _realtime.ReceiptUpdated += OnReceipt;
        _realtime.Reconnecting += _ =>
            Dispatcher.UIThread.Post(() => _status.Text = "Realtime reconnecting. Messages remain server-authoritative.");
        _realtime.Reconnected += _ =>
            Dispatcher.UIThread.Post(() => _ = ExecuteAsync(RefreshAfterReconnectAsync));
        _realtime.Closed += _ =>
            Dispatcher.UIThread.Post(() => _status.Text = "Realtime disconnected. Use refresh to catch up.");

        _selected.Text = "Choose a person or conversation.";
        _status.Text = $"Authenticated device session. Expires {_connection.Session.ExpiresUtc.LocalDateTime:g}.";
        SetControls();
    }

    public async Task InitializeAsync()
    {
        await ExecuteAsync(RefreshListsAsync);
        try
        {
            await _realtime.StartAsync();
            _status.Text = "Realtime connected. Conversation history is loaded from the server.";
        }
        catch (Exception)
        {
            _status.Text = "Realtime could not connect; authenticated HTTP messaging and manual refresh remain available.";
        }
    }

    private async Task ExecuteAsync(Func<Task> action)
    {
        if (_disposed || _busy)
            return;
        _busy = true;
        SetControls();
        try { await action(); }
        catch (UnauthorizedAccessException)
        {
            _status.Text = "Session authorization rejected. Reopen the Join tab and renew the paired device.";
        }
        catch (Exception ex)
        {
            _status.Text = ex is HttpRequestException
                ? "Network request failed. Your unconfirmed draft remains in the editor; retry after reconnect."
                : $"Operation failed: {ex.Message}";
        }
        finally
        {
            _busy = false;
            SetControls();
        }
    }

    private async Task RefreshListsAsync()
    {
        var users = await _api.ListUsersAsync();
        var summaries = await _api.ListDirectConversationSummariesAsync();
        _directory.ItemsSource = users
            .Where(u => u.UserId != _connection.Session.UserId)
            .OrderBy(u => u.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(u => new UserEntry(u))
            .ToArray();
        _inbox.ItemsSource = summaries
            .OrderByDescending(s => s.LastMessage?.CreatedUtc ?? s.CreatedUtc)
            .Select(s => new InboxEntry(s))
            .ToArray();
        _status.Text = $"Loaded {summaries.Count} server conversations and {users.Count} directory users.";
    }

    private async Task OpenSelectedUserAsync()
    {
        if (_directory.SelectedItem is not UserEntry user)
            throw new InvalidOperationException("Select a person from the server directory.");
        var direct = await _api.OpenDirectConversationAsync(user.Value.UserId);
        await SelectConversationAsync(direct.ConversationId, direct.OtherUser.DisplayName);
        await RefreshListsAsync();
    }

    private async Task SelectConversationAsync(Guid conversationId, string title)
    {
        _conversationId = conversationId;
        _selected.Text = $"Conversation: {title}";
        _retryBody = null;
        _retryClientMessageId = null;
        await RefreshThreadAsync();
    }

    private async Task RefreshThreadAsync()
    {
        if (_conversationId is not { } id)
            return;

        var page = await _api.GetDirectRecentHistoryPageAsync(id, limit: 50);
        _history.Clear();
        _history.AddRange(page.Items);
        _olderBeforeMessageId = page.OlderBeforeMessageId;
        RenderMessages();

        // Reading is an actual recipient action: only mark messages read when
        // the selected authenticated conversation has been opened/refreshed.
        await _api.MarkDirectConversationReadAsync(id);
        _status.Text = $"Loaded {_history.Count} persisted messages from the selected conversation.";
    }

    private async Task LoadOlderAsync()
    {
        if (_conversationId is not { } id || _olderBeforeMessageId is not { } cursor)
            return;
        var page = await _api.GetDirectRecentHistoryPageAsync(
            id, beforeMessageId: cursor, limit: 50);
        _history.InsertRange(0, page.Items);
        _olderBeforeMessageId = page.OlderBeforeMessageId;
        RenderMessages();
    }

    private void RenderMessages()
    {
        var ordered = _history
            .DistinctBy(m => m.MessageId)
            .OrderBy(m => m.CreatedUtc)
            .ThenBy(m => m.MessageId)
            .Select(m => new MessageEntry(m, m.SenderUserId == _connection.Session.UserId))
            .ToArray();
        _messages.ItemsSource = ordered;
        _older.IsEnabled = !_busy && _olderBeforeMessageId is not null;
    }

    private async Task SendAsync()
    {
        if (_conversationId is not { } id)
            throw new InvalidOperationException("Choose a conversation.");
        var text = _composer.Text ?? "";
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Enter a message before sending.");

        // Preserve the same idempotency key for a retry of the same draft body.
        if (_retryClientMessageId is null || _retryBody != text)
        {
            _retryBody = text;
            _retryClientMessageId = Guid.NewGuid();
        }

        var confirmed = await _api.SendDirectMessageAsync(
            id, new SendMessageRequest(_retryClientMessageId.Value, text));

        _retryClientMessageId = null;
        _retryBody = null;
        _composer.Text = "";
        _history.Add(confirmed);
        RenderMessages();
        await RefreshListsAsync();
        _status.Text = "Message persisted by the server.";
    }

    private async Task RefreshAfterReconnectAsync()
    {
        await RefreshListsAsync();
        await RefreshThreadAsync();
        _status.Text = "Realtime restored and server history reconciled.";
    }

    private void OnMessage(MessageResponse message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed)
                return;
            _ = ExecuteAsync(
                message.ScopeId == _conversationId
                    ? RefreshThreadAsync
                    : RefreshListsAsync);
        });
    }

    private void OnDelete(MessageDeletedResponse deleted)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!_disposed && deleted.ConversationId == _conversationId)
                _ = ExecuteAsync(RefreshThreadAsync);
        });
    }

    private void OnReceipt(MessageReceiptsChangedResponse receipt)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!_disposed && receipt.ConversationId == _conversationId)
                _status.Text = "Delivery/read receipts updated by the server.";
        });
    }

    private void SetControls()
    {
        _refresh.IsEnabled = !_busy && !_disposed;
        _open.IsEnabled = !_busy && !_disposed;
        _send.IsEnabled = !_busy && !_disposed && _conversationId is not null;
        _reloadThread.IsEnabled = !_busy && !_disposed && _conversationId is not null;
        _older.IsEnabled = !_busy && !_disposed && _olderBeforeMessageId is not null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        _realtime.MessageCreated -= OnMessage;
        _realtime.MessageEdited -= OnMessage;
        _realtime.MessageDeleted -= OnDelete;
        _realtime.ReceiptUpdated -= OnReceipt;
        try { await _realtime.StopAsync(); }
        catch { /* Disconnection should not block cleanup. */ }
        await _realtime.DisposeAsync();
    }

    private sealed record UserEntry(UserSummaryResponse Value)
    {
        public override string ToString() => $"{Value.DisplayName} (@{Value.Username})";
    }
    private sealed record InboxEntry(DirectConversationSummaryResponse Value)
    {
        public override string ToString() =>
            $"{(Value.IsPinned ? "★ " : "")}{Value.OtherUser.DisplayName} · {Value.UnreadCount} unread\n" +
            (Value.LastMessage is null ? "(no messages)" :
            Value.LastMessage.DeletedUtc is not null ? "(message removed)" : Value.LastMessage.Body);
    }
    private sealed record MessageEntry(MessageResponse Value, bool Mine)
    {
        public override string ToString() =>
            $"{(Mine ? "You" : "Them")} · {Value.CreatedUtc.LocalDateTime:g}" +
            (Value.EditedUtc is not null ? " (edited)" : "") + "\n" +
            (Value.DeletedUtc is not null ? "(deleted)" : Value.Body);
    }

    private static TextBlock Heading(string text) => new() { Text = text, FontSize = 18, FontWeight = FontWeight.Bold };
    private static Button Button(string text) => new() { Content = text, MinHeight = 36 };
}
