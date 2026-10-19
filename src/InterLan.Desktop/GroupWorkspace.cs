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
    private readonly ListBox _directory = new() { Height = 115 };
    private readonly ListBox _events = new() { Height = 160 };
    private readonly TextBox _requestedRole = new() { Text = "MEMBER", Watermark = "MEMBER or ADMIN" };
    private readonly TextBox _name = new() { Watermark = "Group name" };
    private readonly TextBox _topic = new() { Watermark = "Optional group topic" };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _identity = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _refresh = Button("Refresh my groups");
    private readonly Button _create = Button("Create group");
    private readonly Button _save = Button("Save selected group metadata");
    private readonly Button _addMember = Button("Add selected server user");
    private readonly Button _removeMember = Button("Remove selected member");
    private readonly Button _promote = Button("Promote selected member to ADMIN");
    private readonly Button _demote = Button("Demote selected ADMIN to MEMBER");
    private readonly Button _loadEvents = Button("Refresh member-only audit events");
    private readonly Guid _currentUserId;
    private Guid? _deleteConfirmationUserId;
    private GroupDetailsResponse? _selected;
    private bool _busy;

    public GroupWorkspace(PairedClientConnection connection)
    {
        _api = connection.CreateApiClient();
        _currentUserId = connection.Session.UserId;
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
        right.Children.Add(Heading("Add a user to this group"));
        right.Children.Add(new TextBlock {
            Text = "Select an existing authenticated server user. The server enforces group-role authority.",
            TextWrapping = TextWrapping.Wrap });
        right.Children.Add(_directory);
        right.Children.Add(_requestedRole);
        right.Children.Add(_addMember);
        right.Children.Add(Heading("Member administration"));
        right.Children.Add(_removeMember);
        right.Children.Add(_promote);
        right.Children.Add(_demote);
        right.Children.Add(Heading("Group audit events"));
        right.Children.Add(_loadEvents);
        right.Children.Add(_events);
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
        _addMember.Click += async (_, _) => await ExecuteAsync(AddMemberAsync);
        _removeMember.Click += async (_, _) => await ExecuteAsync(RemoveMemberAsync);
        _promote.Click += async (_, _) => await ExecuteAsync(() => ChangeMemberRoleAsync("ADMIN"));
        _demote.Click += async (_, _) => await ExecuteAsync(() => ChangeMemberRoleAsync("MEMBER"));
        _loadEvents.Click += async (_, _) => await ExecuteAsync(LoadEventsAsync);
        _members.SelectionChanged += (_, _) => _deleteConfirmationUserId = null;
        _groups.SelectionChanged += async (_, _) =>
        {
            if (_groups.SelectedItem is GroupEntry selected)
                await ExecuteAsync(() => LoadGroupAsync(selected.Value.GroupId));
        };
        _status.Text = "Groups are authorized and persisted by the canonical server. Group messaging and attachments require additional merged backend routes.";
        SetControls();
    }

    public Task InitializeAsync() => ExecuteAsync(async () =>
    {
        await RefreshGroupsAsync();
        var users = await _api.ListUsersAsync();
        _directory.ItemsSource = users
            .Where(u => u.UserId != _currentUserId)
            .OrderBy(u => u.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(u => new UserEntry(u))
            .ToArray();
    });

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
        _deleteConfirmationUserId = null;
        _status.Text = $"{details.Members.Count} members returned by authorized group detail API.";
        await LoadEventsAsync();
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

    private async Task AddMemberAsync()
    {
        var group = _selected ??
            throw new InvalidOperationException("Select a group first.");
        if (group.MyRole is not ("OWNER" or "ADMIN"))
            throw new UnauthorizedAccessException("Only group owner/admin can invite members.");
        if (_directory.SelectedItem is not UserEntry user)
            throw new InvalidOperationException("Select a server user from the directory.");
        var role = (_requestedRole.Text ?? "").Trim().ToUpperInvariant();
        if (role is not ("MEMBER" or "ADMIN"))
            throw new ArgumentException("An assignable group role is MEMBER or ADMIN.");
        if (role == "ADMIN" && group.MyRole != "OWNER")
            throw new UnauthorizedAccessException("Only group owner can appoint admins.");

        using var response = await _api.HttpClient.PostAsJsonAsync(
            $"/api/v1/groups/{group.GroupId:D}/members",
            new AddGroupMemberRequest(user.Value.UserId, role));
        response.EnsureSuccessStatusCode();
        await LoadGroupAsync(group.GroupId);
        _status.Text = "Server confirmed group membership creation.";
    }

    private async Task RemoveMemberAsync()
    {
        var group = _selected ??
            throw new InvalidOperationException("Select a group first.");
        if (_members.SelectedItem is not MemberEntry member)
            throw new InvalidOperationException("Select a group member to remove.");
        if (member.Value.Role == "OWNER")
            throw new UnauthorizedAccessException("The group owner cannot be removed.");
        if (member.Value.UserId != _currentUserId &&
            group.MyRole is not ("OWNER" or "ADMIN"))
            throw new UnauthorizedAccessException("Only group owner/admin can remove another member.");
        if (group.MyRole == "ADMIN" && member.Value.Role == "ADMIN" &&
            member.Value.UserId != _currentUserId)
            throw new UnauthorizedAccessException("Admin cannot remove another admin.");
        if (_deleteConfirmationUserId != member.Value.UserId)
        {
            _deleteConfirmationUserId = member.Value.UserId;
            _status.Text = "Select Remove selected member a second time to confirm this server-side revocation.";
            return;
        }

        using var response = await _api.HttpClient.DeleteAsync(
            $"/api/v1/groups/{group.GroupId:D}/members/{member.Value.UserId:D}");
        response.EnsureSuccessStatusCode();
        _deleteConfirmationUserId = null;
        if (member.Value.UserId == _currentUserId)
        {
            _selected = null;
            _identity.Text = "You have left this group.";
            _members.ItemsSource = null;
            _events.ItemsSource = null;
            await RefreshGroupsAsync();
        }
        else
        {
            await LoadGroupAsync(group.GroupId);
        }
        _status.Text = "Member revocation acknowledged by the canonical server.";
    }

    private async Task ChangeMemberRoleAsync(string role)
    {
        var group = _selected ??
            throw new InvalidOperationException("Select a group first.");
        if (group.MyRole != "OWNER")
            throw new UnauthorizedAccessException("Only the group owner can change member roles.");
        if (_members.SelectedItem is not MemberEntry member)
            throw new InvalidOperationException("Select a group member.");
        if (member.Value.Role == "OWNER")
            throw new UnauthorizedAccessException("Group owner role is immutable.");
        using var response = await _api.HttpClient.PutAsJsonAsync(
            $"/api/v1/groups/{group.GroupId:D}/members/{member.Value.UserId:D}/role",
            new UpdateGroupMemberRoleRequest(role));
        response.EnsureSuccessStatusCode();
        await LoadGroupAsync(group.GroupId);
        _status.Text = $"Server confirmed member role {role}.";
    }

    private async Task LoadEventsAsync()
    {
        var group = _selected ??
            throw new InvalidOperationException("Select a group before loading audit events.");
        var events = await _api.HttpClient.GetFromJsonAsync<GroupEventResponse[]>(
            $"/api/v1/groups/{group.GroupId:D}/events?limit=100") ?? [];
        _events.ItemsSource = events
            .OrderByDescending(e => e.CreatedUtc)
            .Select(e => new EventEntry(e))
            .ToArray();
    }

    private void SetControls()
    {
        _refresh.IsEnabled = !_busy;
        _create.IsEnabled = !_busy;
        _save.IsEnabled = !_busy && (_selected?.MyRole is "OWNER" or "ADMIN");
        _addMember.IsEnabled = !_busy && (_selected?.MyRole is "OWNER" or "ADMIN");
        _removeMember.IsEnabled = !_busy && _selected is not null;
        _promote.IsEnabled = !_busy && _selected?.MyRole == "OWNER";
        _demote.IsEnabled = !_busy && _selected?.MyRole == "OWNER";
        _loadEvents.IsEnabled = !_busy && _selected is not null;
    }

    private sealed record EventEntry(GroupEventResponse Value)
    {
        public override string ToString() =>
            $"{Value.CreatedUtc.LocalDateTime:g} · {Value.EventType} · actor {Value.ActorUserId?.ToString("D") ?? "system"}";
    }
    private sealed record UserEntry(UserSummaryResponse Value)
    {
        public override string ToString() =>
            $"{Value.DisplayName} (@{Value.Username})";
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
