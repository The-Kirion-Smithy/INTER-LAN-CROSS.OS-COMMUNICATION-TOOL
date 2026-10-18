namespace InterLan.Domain;

/// <summary>
/// Canonical P3 membership policy. Server account roles do not grant group powers.
/// An OWNER is never silently transferred, demoted, or removed.
/// </summary>
public static class GroupRolePolicy
{
    public const string Owner = "OWNER";
    public const string Admin = "ADMIN";
    public const string Member = "MEMBER";

    public static string NormalizeAssignableRole(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Group role is required.", nameof(value));

        var role = value.Trim().ToUpperInvariant();
        return role is Admin or Member
            ? role
            : throw new ArgumentException("Assignable role must be ADMIN or MEMBER.", nameof(value));
    }

    public static void RequireMetadataEditor(string actorRole)
    {
        if (actorRole is not Owner and not Admin)
            throw new UnauthorizedAccessException("Group owner or admin authority is required.");
    }

    public static void RequireCanAdd(string actorRole, string requestedRole)
    {
        RequireMetadataEditor(actorRole);
        if (requestedRole == Admin && actorRole != Owner)
            throw new UnauthorizedAccessException("Only the group owner can appoint an admin.");
    }

    public static void RequireCanRemove(string actorRole, string targetRole, bool removingSelf)
    {
        if (targetRole == Owner)
            throw new UnauthorizedAccessException("The group owner cannot be removed.");

        if (removingSelf)
            return;

        RequireMetadataEditor(actorRole);
        if (actorRole == Admin && targetRole != Member)
            throw new UnauthorizedAccessException("An admin cannot remove another admin.");
    }

    public static void RequireOwner(string actorRole)
    {
        if (actorRole != Owner)
            throw new UnauthorizedAccessException("Only the group owner can change member roles.");
    }

    public static void RequireMutableTarget(string targetRole)
    {
        if (targetRole == Owner)
            throw new UnauthorizedAccessException("The group owner role cannot be changed.");
    }
}
