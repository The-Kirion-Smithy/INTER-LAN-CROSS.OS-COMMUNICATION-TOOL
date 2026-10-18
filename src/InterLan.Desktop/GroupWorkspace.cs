using System.Net;
using System.Net.Http.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using InterLan.Application;
using InterLan.Contracts;

namespace InterLan.Desktop;

/// <summary>
/// Group metadata is server-owned. This screen only displays membership
/// returned for the currently authenticated paired device.
/// </summary>
internal sealed class GroupWorkspace : UserControl
{
    private readonly InterLanApiClient _api;
    private readonly ListBox _groups = new() { Height = 250 };
    private readonly ListBox _members = new() { Height = 250 };
    private readonly TextBox _name = new() { PlaceholderText = "Group name" };
    private readonly TextBox _topic = new() { PlaceholderText = "Optional group topic" };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _identity = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _refresh = Button("Refresh my groups");
    private readonly Button _create = Button("Create group");
    private readonly Button _save = Button("Save selected group metadata");
    private GroupDetailsResponse? _selected;
    private bool _busy;

    public GroupWorkspace(PairedClientConnection connection)
    {
        _api = connection.CreateApiClient();
        var left = new StackPanel { Margin = new Thickness(10), Spacing = 10 };
        left.Children.Add(Heading("My groups"));
        left.Children.Add(new TextBlock {
            Text = "Only server-authorized group membership is listed. This screen does not create fake group messages.",
            TextWrapping = TextWrapping.Wrap });
        left.Children.Add(_refresh);
        left.Children.Add(_groups);
        left.Children.Add(Heading("Create or edit group"));
        left.Children.Add(_name);
        left.Children.Add(_topic);
        left.Children.Add(_create);
        left.Children.Add(_save);

        var right = new StackPanel { Margin = new Thickness(10), Spacing = 10 };
        right.Children.Add(Heading("Group details and membership"));
        right.Children.Add(_identity);
        right.Children.Add(_members);
        right.Children.Add(_status);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("320,*") };
        var leftScroll = new ScrollViewer { Content = left };
        var rightScroll = new ScrollViewer { Content = right };
        Grid.SetColumn(rightScroll, 1);
        grid.Children.Add(leftScroll);
        grid.Children.Add(rightScroll);
        Content = grid;

        _refresh.Click += async (_, _) => await ExecuteAsync(RefreshGroupsAsync);
        _create.Click += async (_, _) => await ExecuteAsync(CreateAsync);
        _save.Click += async (_, _) => await ExecuteAsync(UpdateAsync);
        _groups.SelectionChanged += async (_, _) =>
        {
            if (_groups.SelectedItem is GroupEntry selected)
                await ExecuteAsync(() => LoadGroupAsync(selected.Value.GroupId));
        };
        _status.Text = "Groups are authorized and persisted by the canonical server. Group messaging and attachments require additional merged backend routes.";
        SetControls();
    }

    public Task InitializeAsync() => ExecuteAsync(RefreshGroupsAsync);

    private async Task ExecuteAsync(Func<Task> work)
    {
        if (_busy) return;
        _busy = true;
        SetControls();
        try { await work(); }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            _status.Text = "Device session is expired or revoked. Restore pairing before managing groups.";
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
        {
            _status.Text = "Group membership or role does not authorize that action.";
        }
        catch (HttpRequestException)
        {
            _status.Text = "Network/server operation failed. No local group membership has been changed.";
        }
        catch (Exception ex)
        {
            _status.Text = $"Group operation failed: {ex.Message}";
        }
        finally { _busy = false; SetControls(); }
    }

    private async Task RefreshGroupsAsync()
    {
        var result = await _api.HttpClient.GetFromJsonAsync<GroupSummaryResponse[]>(
            "/api/v1/groups") ?? [];
        _groups.ItemsSource = result
            .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => new GroupEntry(g))
            .ToArray();
        _status.Text = $"{result.Length} server-authorized group memberships returned.";
    }

    private async Task LoadGroupAsync(Guid groupId)
    {
        var details = await _api.HttpClient.GetFromJsonAsync<GroupDetailsResponse>(
            $"/api/v1/groups/{groupId:D}")
            ?? throw new InvalidDataException("Group detail response was empty.");
        _selected = details;
        _name.Text = details.Name;
        _topic.Text = details.Topic ?? "";
        _identity.Text =
            $"{details.Name}\nGroup ID: {details.GroupId:D}\nMy group role: {details.MyRole}\n" +
            $"Created by: {details.CreatedByUserId:D} · {details.CreatedUtc.LocalDateTime:g}";
        _members.ItemsSource = details.Members
            .OrderBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(m => new MemberEntry(m))
            .ToArray();
        _status.Text = $"{details.Members.Count} members returned by authorized group detail API.";
    }

    private async Task CreateAsync()
    {
        if (string.IsNullOrWhiteSpace(_name.Text))
            throw new ArgumentException("Enter a group name.");
        using var response = await _api.HttpClient.PostAsJsonAsync(
            "/api/v1/groups",
            new CreateGroupRequest(_name.Text.Trim(),
                string.IsNullOrWhiteSpace(_topic.Text) ? null : _topic.Text.Trim()));
        response.EnsureSuccessStatusCode();
        _selected = null;
        await RefreshGroupsAsync();
        _status.Text = "Group creation confirmed by the canonical server. Select the new group to inspect membership.";
    }

    private async Task UpdateAsync()
    {
        if (_selected is not { } group)
            throw new InvalidOperationException("Select a group to edit.");
        if (group.MyRole is not ("OWNER" or "ADMIN"))
            throw new UnauthorizedAccessException("Group metadata can only be edited by group owner/admin.");
        if (string.IsNullOrWhiteSpace(_name.Text))
            throw new ArgumentException("Group name is required.");
        using var response = await _api.HttpClient.PutAsJsonAsync(
            $"/api/v1/groups/{group.GroupId:D}",
            new UpdateGroupRequest(_name.Text.Trim(),
                string.IsNullOrWhiteSpace(_topic.Text) ? null : _topic.Text.Trim()));
        response.EnsureSuccessStatusCode();
        await LoadGroupAsync(group.GroupId);
        await RefreshGroupsAsync();
        _status.Text = "Group metadata saved by the canonical server.";
    }

    private void SetControls()
    {
        _refresh.IsEnabled = !_busy;
        _create.IsEnabled = !_busy;
        _save.IsEnabled = !_busy && _selected?.MyRole is "OWNER" or "ADMIN";
    }

    private sealed record GroupEntry(GroupSummaryResponse Value)
    {
        public override string ToString() =>
            $"{Value.Name} · role {Value.MyRole} · {Value.GroupId:D}";
    }
    private sealed record MemberEntry(GroupMemberResponse Value)
    {
        public override string ToString() =>
            $"{Value.DisplayName} (@{Value.Username}) · {Value.Role}";
    }
    private static TextBlock Heading(string text) =>
        new() { Text = text, FontWeight = FontWeight.Bold, FontSize = 19 };
    private static Button Button(string text) => new() { Content = text, MinHeight = 38 };
}
