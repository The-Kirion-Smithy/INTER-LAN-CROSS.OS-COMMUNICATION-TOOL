namespace InterLan.Contracts;

public sealed record UserSummaryResponse(
    Guid UserId,
    string Username,
    string DisplayName,
    string Role);

public sealed record OpenDirectConversationRequest(Guid OtherUserId);

public sealed record DirectConversationResponse(
    Guid ConversationId,
    UserSummaryResponse OtherUser,
    DateTimeOffset CreatedUtc);

public sealed record SendMessageRequest(
    Guid ClientMessageId,
    string Body,
    Guid? ReplyToMessageId = null);

public sealed record MessageResponse(
    Guid MessageId,
    string ScopeType,
    Guid ScopeId,
    Guid SenderUserId,
    Guid ClientMessageId,
    string Body,
    Guid? ReplyToMessageId,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? EditedUtc = null,
    DateTimeOffset? DeletedUtc = null);

public sealed record PersistedMessageResult(
    MessageResponse Message,
    bool Created);


public sealed record MessageReceiptResponse(
    Guid MessageId,
    Guid UserId,
    DateTimeOffset? DeliveredUtc,
    DateTimeOffset? ReadUtc);

public sealed record DirectConversationSummaryResponse(
    Guid ConversationId,
    UserSummaryResponse OtherUser,
    DateTimeOffset CreatedUtc,
    MessageResponse? LastMessage,
    int UnreadCount,
    bool IsPinned = false,
    DateTimeOffset? MutedUntilUtc = null,
    bool IsArchived = false);

public sealed record MessagePageResponse(
    IReadOnlyList<MessageResponse> Items,
    Guid? NextAfterMessageId,
    bool HasMore);

public sealed record RealtimeTicketResponse(
    string Ticket,
    DateTimeOffset ExpiresUtc);

public sealed record EditMessageRequest(string Body);

public sealed record MessageDeletedResponse(
    Guid MessageId,
    Guid ConversationId,
    DateTimeOffset DeletedUtc);

public sealed record MarkConversationReadRequest(
    Guid? UpToMessageId = null);

public sealed record ConversationReadResponse(
    Guid ConversationId,
    int MarkedCount,
    DateTimeOffset ReadUtc);

public sealed record MessageReceiptsChangedResponse(
    Guid ConversationId,
    Guid MessageId,
    IReadOnlyList<MessageReceiptResponse> Receipts);

public sealed record RecentMessagePageResponse(
    IReadOnlyList<MessageResponse> Items,
    Guid? OlderBeforeMessageId,
    bool HasOlder);

public sealed record DirectConversationActivityResponse(
    Guid ConversationId,
    DateTimeOffset ActivityUtc,
    Guid? LastMessageId,
    int UnreadCount);

public sealed record DirectConversationPreferenceResponse(
    Guid ConversationId,
    bool IsPinned,
    DateTimeOffset? MutedUntilUtc,
    bool IsArchived,
    DateTimeOffset UpdatedUtc);

public sealed record UpdateDirectConversationPreferenceRequest(
    bool IsPinned,
    DateTimeOffset? MutedUntilUtc,
    bool IsArchived);

public sealed record DirectMessageSearchResponse(
    Guid ConversationId,
    string Query,
    IReadOnlyList<MessageResponse> Items);

public sealed record UserPresenceResponse(
    Guid UserId,
    bool IsOnline,
    int ConnectionCount,
    DateTimeOffset ObservedUtc);

public sealed record TypingIndicatorResponse(
    Guid ConversationId,
    Guid UserId,
    bool IsTyping,
    DateTimeOffset ObservedUtc);

public sealed record UserDirectorySearchResponse(
    string Query,
    IReadOnlyList<UserSummaryResponse> Users);

public sealed record DirectUnreadSummaryResponse(
    int TotalUnreadMessages,
    int ConversationsWithUnread);

public sealed record DirectGlobalMessageSearchHitResponse(
    Guid ConversationId,
    UserSummaryResponse OtherUser,
    MessageResponse Message);

public sealed record DirectGlobalMessageSearchResponse(
    string Query,
    IReadOnlyList<DirectGlobalMessageSearchHitResponse> Hits);

public sealed record CreateGroupRequest(
    string Name,
    string? Topic = null);

public sealed record GroupSummaryResponse(
    Guid GroupId,
    string Name,
    string? Topic,
    string MyRole,
    DateTimeOffset CreatedUtc);

public sealed record GroupMemberResponse(
    Guid UserId,
    string Username,
    string DisplayName,
    string Role,
    DateTimeOffset JoinedUtc);

public sealed record GroupDetailsResponse(
    Guid GroupId,
    string Name,
    string? Topic,
    Guid CreatedByUserId,
    DateTimeOffset CreatedUtc,
    string MyRole,
    IReadOnlyList<GroupMemberResponse> Members);

public sealed record UpdateGroupRequest(
    string Name,
    string? Topic);

public sealed record AddGroupMemberRequest(
    Guid UserId,
    string Role = "MEMBER");

public sealed record UpdateGroupMemberRoleRequest(
    string Role);

public sealed record GroupMembershipMutationResponse(
    Guid GroupId,
    Guid UserId,
    string? Role,
    string Status,
    DateTimeOffset ChangedUtc);
