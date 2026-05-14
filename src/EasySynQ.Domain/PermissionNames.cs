namespace EasySynQ.Domain;

/// <summary>
/// Code-side constants mirroring the seeded
/// <see cref="EasySynQ.Domain.Entities.Identity.Permission"/> catalog (ADR 0007).
/// Every authorization check site must refer to one of these constants;
/// raw string literals are forbidden outside this class and the seeding
/// migration. Enforcement is by code review until misuse appears, at which
/// point an analyzer rule is considered.
/// </summary>
/// <remarks>
/// <para>
/// The constant values match the seeded <c>Permission.Name</c> rows
/// exactly. The Phase 1 migration that creates the <c>Permissions</c>
/// table inserts the eleven rows below in the same migration. Each
/// subsequent phase adds its permissions (and the matching code-side
/// constants here) in its own migration.
/// </para>
/// <para>
/// <see cref="All"/> exposes the canonical ordered list of Phase 1
/// permission names so bootstrap-time consumers can fetch the matching
/// rows in a single query.
/// </para>
/// </remarks>
public static class PermissionNames
{
    /// <summary>Top-level system administration capability.</summary>
    public const string SystemAdminister = "System.Administer";

    /// <summary>Create new role definitions.</summary>
    public const string RoleCreate = "Role.Create";

    /// <summary>Edit existing role names or descriptions.</summary>
    public const string RoleEdit = "Role.Edit";

    /// <summary>Soft-delete role definitions.</summary>
    public const string RoleDelete = "Role.Delete";

    /// <summary>Attach permissions to or detach permissions from a role.</summary>
    public const string RoleAssignPermissions = "Role.AssignPermissions";

    /// <summary>Create new user accounts.</summary>
    public const string UserCreate = "User.Create";

    /// <summary>Edit user-account fields (display name, username, etc.).</summary>
    public const string UserEdit = "User.Edit";

    /// <summary>Administratively disable a user account.</summary>
    public const string UserDisable = "User.Disable";

    /// <summary>Attach roles to or detach roles from a user.</summary>
    public const string UserAssignRoles = "User.AssignRoles";

    /// <summary>Grant permissions directly to a user, bypassing role membership.</summary>
    public const string UserGrantPermissions = "User.GrantPermissions";

    /// <summary>Read the append-only audit log.</summary>
    public const string AuditLogRead = "AuditLog.Read";

    /// <summary>
    /// Canonical ordered list of every Phase 1 permission name. Used by
    /// the bootstrap path to fetch the seeded rows in one query.
    /// </summary>
    public static IReadOnlyList<string> All { get; } =
    [
        SystemAdminister,
        RoleCreate,
        RoleEdit,
        RoleDelete,
        RoleAssignPermissions,
        UserCreate,
        UserEdit,
        UserDisable,
        UserAssignRoles,
        UserGrantPermissions,
        AuditLogRead,
    ];
}
