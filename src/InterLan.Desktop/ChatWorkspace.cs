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
    private readonly TextBox _composer = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 78, PlaceholderText = "Write a direct message" };
    private readonly Button _send = Button("Send message");
    private readonly Button _older = Button("Load earlier messages");
    private readonly Button _refresh = Button("Refresh inbox / directory");
    private readonly Button _open = Button("Open direct message with selected person");
    private readonly Button _reloadThread = Button("Refresh selected conversation");
    private readonly TextBox _searchInput = new() { PlaceholderText = "Search this conversation" };
    private readonly ListBox _searchResults = new() { Height = 110 };
    private readonly TextBox _editInput = new() { PlaceholderText = "Edit your selected message" };
    private readonly Button _searchButton = Button("Search server history");
    private readonly Button _reply = Button("Reply to selected message");
    private readonly Button _edit = Button("Save edit to selected own message");
    private readonly Button _delete = Button("Delete selected own message");
    private readonly Button _receipts = Button("Show selected message receipts");
    private readonly Button _pin = Button("Pin conversation");
    private readonly Button _archive = Button("Archive conversation");
    private readonly List<MessageResponse> _history = [];
    private Guid? _conversationId;
    private Guid? _replyToMessageId;
    private Guid? _confirmDeleteId;
    private DirectConversationPreferenceResponse? _preference;
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
        var preferences = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        preferences.Children.Add(_pin);
        preferences.Children.Add(_archive);
        right.Children.Add(preferences);
        right.Children.Add(_older);
        right.Children.Add(_messages);
        right.Children.Add(_receipts);
        right.Children.Add(_reply);
        right.Children.Add(_editInput);
        var editButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        editButtons.Children.Add(_edit);
        editButtons.Children.Add(_delete);
        right.Children.Add(editButtons);
        right.Children.Add(_searchInput);
        right.Children.Add(_searchButton);
        right.Children.Add(_searchResults);
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
        _searchButton.Click += async (_, _) => await ExecuteAsync(SearchAsync);
        _reply.Click += (_, _) => ChooseReply();
        _edit.Click += async (_, _) => await ExecuteAsync(EditSelectedAsync);
        _delete.Click += async (_, _) => await ExecuteAsync(DeleteSelectedAsync);
        _receipts.Click += async (_, _) => await ExecuteAsync(ShowReceiptsAsync);
        _pin.Click += async (_, _) => await ExecuteAsync(() => UpdatePreferenceAsync(togglePin: true));
        _archive.Click += async (_, _) => await ExecuteAsync(() => UpdatePreferenceAsync(togglePin: false));
        _messages.SelectionChanged += (_, _) =>
        {
            _confirmDeleteId = null;
            if (_messages.SelectedItem is MessageEntry selected &&
                selected.Mine && selected.Value.DeletedUtc is null)
                _editInput.Text = selected.Value.Body;
            else
                _editInput.Text = "";
        };
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
        // Keep the event's connection-id parameter distinct from C#'s discard.
        // Otherwise `_ = ExecuteAsync(...)` tries to assign a Task to a string.
        _realtime.Reconnected += connectionId =>
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
            if (ex is HttpRequestException { StatusCode: System.Net.HttpStatusCode.Unauthorized })
            {
                _status.Text = "Server rejected the expired/revoked bearer. Renew the device session on the Join tab.";
                return;
            }
            if (ex is HttpRequestException { StatusCode: System.Net.HttpStatusCode.Forbidden })
            {
                _status.Text = "Server denied this conversation operation. Membership or message policy may have changed.";
                return;
            }
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
        _replyToMessageId = null;
        _confirmDeleteId = null;
        _searchResults.ItemsSource = null;
        _preference = await _api.GetDirectConversationPreferenceAsync(conversationId);
        _pin.Content = _preference.IsPinned ? "Unpin conversation" : "Pin conversation";
        _archive.Content = _preference.IsArchived ? "Unarchive conversation" : "Archive conversation";
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
            id, new SendMessageRequest(_retryClientMessageId.Value, text, _replyToMessageId));

        _retryClientMessageId = null;
        _retryBody = null;
        _composer.Text = "";
        _replyToMessageId = null;
        _history.Add(confirmed);
        RenderMessages();
        await RefreshListsAsync();
        _status.Text = "Message persisted by the server.";
    }

    private void ChooseReply()
    {
        if (_messages.SelectedItem is not MessageEntry selected ||
            selected.Value.DeletedUtc is not null)
        {
            _status.Text = "Select an existing message to reply.";
            return;
        }
        _replyToMessageId = selected.Value.MessageId;
        _status.Text = $"Replying to server message {_replyToMessageId:D}. Your draft will reference that message.";
        _composer.Focus();
    }

    private async Task EditSelectedAsync()
    {
        if (_conversationId is not { } id ||
            _messages.SelectedItem is not MessageEntry selected ||
            !selected.Mine || selected.Value.DeletedUtc is not null)
            throw new InvalidOperationException("Select one of your existing messages to edit.");
        var newBody = _editInput.Text ?? "";
        if (string.IsNullOrWhiteSpace(newBody))
            throw new ArgumentException("Edited content cannot be empty.");
        await _api.EditDirectMessageAsync(id, selected.Value.MessageId, newBody);
        await RefreshThreadAsync();
        _status.Text = "Message edit confirmed by server.";
    }

    private async Task DeleteSelectedAsync()
    {
        if (_conversationId is not { } id ||
            _messages.SelectedItem is not MessageEntry selected ||
            !selected.Mine || selected.Value.DeletedUtc is not null)
            throw new InvalidOperationException("Select one of your existing messages to delete.");
        if (_confirmDeleteId != selected.Value.MessageId)
        {
            _confirmDeleteId = selected.Value.MessageId;
            _status.Text = "Click Delete selected own message again to confirm server-side deletion.";
            return;
        }
        await _api.DeleteDirectMessageAsync(id, selected.Value.MessageId);
        _confirmDeleteId = null;
        await RefreshThreadAsync();
        _status.Text = "Message deletion confirmed by server.";
    }

    private async Task ShowReceiptsAsync()
    {
        if (_messages.SelectedItem is not MessageEntry selected)
            throw new InvalidOperationException("Select a message to view receipts.");
        var receipts = await _api.GetMessageReceiptsAsync(selected.Value.MessageId);
        _status.Text = receipts.Count == 0
            ? "No server-confirmed receipts yet."
            : string.Join(" | ", receipts.Select(r =>
                $"user {r.UserId:D}: delivered {(r.DeliveredUtc is null ? "—" : r.DeliveredUtc.Value.LocalDateTime.ToString("g"))}, read {(r.ReadUtc is null ? "—" : r.ReadUtc.Value.LocalDateTime.ToString("g"))}"));
    }

    private async Task SearchAsync()
    {
        if (_conversationId is not { } id)
            throw new InvalidOperationException("Select a conversation first.");
        if (string.IsNullOrWhiteSpace(_searchInput.Text))
            throw new ArgumentException("Enter a message search query.");
        var results = await _api.SearchDirectMessagesAsync(id, _searchInput.Text.Trim());
        _searchResults.ItemsSource = results.Items
            .Select(m => new MessageEntry(m, m.SenderUserId == _connection.Session.UserId))
            .ToArray();
        _status.Text = $"{results.Items.Count} matching persisted messages.";
    }

    private async Task UpdatePreferenceAsync(bool togglePin)
    {
        if (_conversationId is not { } id)
            throw new InvalidOperationException("Select a conversation first.");
        var current = _preference ??
            await _api.GetDirectConversationPreferenceAsync(id);
        _preference = await _api.UpdateDirectConversationPreferenceAsync(
            id, new UpdateDirectConversationPreferenceRequest(
                togglePin ? !current.IsPinned : current.IsPinned,
                current.MutedUntilUtc,
                togglePin ? current.IsArchived : !current.IsArchived));
        _pin.Content = _preference.IsPinned ? "Unpin conversation" : "Pin conversation";
        _archive.Content = _preference.IsArchived ? "Unarchive conversation" : "Archive conversation";
        await RefreshListsAsync();
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
        foreach (var button in new[] { _searchButton, _reply, _edit, _delete,
            _receipts, _pin, _archive })
            button.IsEnabled = !_busy && !_disposed && _conversationId is not null;
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
