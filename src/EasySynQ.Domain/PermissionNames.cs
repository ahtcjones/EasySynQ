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
    /// Physically delete vault blobs (hard-delete the row + remove the
    /// on-disk file). System-administration capability — Phase 2's
    /// vault-permission migration adds it to the catalog and seeds the
    /// Administrator role with it (ADR 0008 C2). Operational document
    /// hard-delete permissions are separate (<see cref="DocumentHardDelete"/>).
    /// </summary>
    public const string VaultPhysicalDelete = "Vault.PhysicalDelete";

    /// <summary>
    /// Canonical ordered list of every currently-defined system
    /// permission — the IT-side capabilities that the bootstrap path
    /// always grants to the Administrator role (ADR 0007 §Decision —
    /// Administrator is reserved for system administration, not
    /// operational superuser). The list grows as later phases add
    /// system-tier capabilities; the bootstrap path consumes it
    /// verbatim, so adding a name here is sufficient to roll the new
    /// permission into every fresh install's Administrator grants.
    /// Phase 2+ operational catalogs are exposed via per-phase lists
    /// (e.g., <see cref="Phase2Document"/>) and are not granted to
    /// Administrator by default.
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
        VaultPhysicalDelete,
    ];

    // ───────────────────────────────────────────────────────────────
    // Phase 2 — Document Controller (ADR 0008)
    // ───────────────────────────────────────────────────────────────

    /// <summary>Create a new internal document (initial Draft revision).</summary>
    public const string DocumentCreate = "Document.Create";

    /// <summary>Edit a document while it is in Draft state.</summary>
    public const string DocumentEditDraft = "Document.EditDraft";

    /// <summary>Move a Draft revision to In Review.</summary>
    public const string DocumentSubmitForReview = "Document.SubmitForReview";

    /// <summary>
    /// Name the reviewer set when submitting for review. May be granted
    /// to authors (small-shop default) or restricted to QM
    /// (strict-gatekeeper policy). Intentionally not assigned to the
    /// seeded QualityManager role by default per ADR 0008.
    /// </summary>
    public const string DocumentAssignReviewers = "Document.AssignReviewers";

    /// <summary>
    /// Sign as a reviewer on a document where the user is in the
    /// assigned-reviewers list.
    /// </summary>
    public const string DocumentReview = "Document.Review";

    /// <summary>Return an In Review document to Draft for author edits.</summary>
    public const string DocumentReturnForEdits = "Document.ReturnForEdits";

    /// <summary>Retire an Active document (no new revision).</summary>
    public const string DocumentRetire = "Document.Retire";

    /// <summary>
    /// Soft-delete a document or revision (with reason) once it is past
    /// the hard-delete boundary.
    /// </summary>
    public const string DocumentSoftDelete = "Document.SoftDelete";

    /// <summary>
    /// Hard-delete a Draft revision that has no signatures and is not
    /// referenced by any signed record.
    /// </summary>
    public const string DocumentHardDelete = "Document.HardDelete";

    /// <summary>
    /// View Superseded and Archived revisions in the UI (not in the
    /// default Active-only list).
    /// </summary>
    public const string DocumentViewArchived = "Document.ViewArchived";

    /// <summary>Add a new External document reference (ASTM, AMS, customer spec).</summary>
    public const string ExternalDocumentCreate = "ExternalDocument.Create";

    /// <summary>Record a new revision of an External document.</summary>
    public const string ExternalDocumentUpdateRevision = "ExternalDocument.UpdateRevision";

    /// <summary>Create or remove links between internal documents and external documents.</summary>
    public const string DocumentLinkManage = "DocumentLink.Manage";

    /// <summary>
    /// Canonical ordered list of every Phase 2 Document Controller
    /// permission name (ADR 0008). The order matches the migration's
    /// seed block and the SPEC §5.1 amendment's catalog enumeration.
    /// The migration-seed test pins equality of seeded names against
    /// this list.
    /// </summary>
    /// <remarks>
    /// <see cref="DocumentAssignReviewers"/> appears in this list (it
    /// is a real catalog permission) but is intentionally not granted
    /// to the seeded QualityManager role — see
    /// <see cref="QualityManagerDefaults"/> for the default-role
    /// permission set.
    /// </remarks>
    public static IReadOnlyList<string> Phase2Document { get; } =
    [
        DocumentCreate,
        DocumentEditDraft,
        DocumentSubmitForReview,
        DocumentAssignReviewers,
        DocumentReview,
        DocumentReturnForEdits,
        DocumentRetire,
        DocumentSoftDelete,
        DocumentHardDelete,
        DocumentViewArchived,
        ExternalDocumentCreate,
        ExternalDocumentUpdateRevision,
        DocumentLinkManage,
    ];

    /// <summary>
    /// Permission names granted to the seeded QualityManager role at
    /// Phase 2 migration time. Equal to <see cref="Phase2Document"/>
    /// minus <see cref="DocumentAssignReviewers"/>. The omission is
    /// deliberate per ADR 0008: <c>Document.AssignReviewers</c> is
    /// granted by organizations either to author roles (small-shop
    /// default) or restricted to QM (strict-gatekeeper policy) —
    /// either choice is a policy decision the seeded data does not
    /// make.
    /// </summary>
    public static IReadOnlyList<string> QualityManagerDefaults { get; } =
    [
        DocumentCreate,
        DocumentEditDraft,
        DocumentSubmitForReview,
        DocumentReview,
        DocumentReturnForEdits,
        DocumentRetire,
        DocumentSoftDelete,
        DocumentHardDelete,
        DocumentViewArchived,
        ExternalDocumentCreate,
        ExternalDocumentUpdateRevision,
        DocumentLinkManage,
    ];
}
