namespace EasySynQ.Domain;

/// <summary>
/// Code-side constants mirroring the canonical strings used in the
/// <see cref="EasySynQ.Domain.Entities.Audit.LockReason.LockedEntityType"/>
/// column and in <c>ILockReasonResolver.LockedEntityType</c> (ADR 0012).
/// </summary>
/// <remarks>
/// <para>
/// Every consumer (lifecycle service writing chain-anchor metadata,
/// resolver registering itself, UI trigger anchoring a popover) must
/// refer to one of these constants. Raw string literals are forbidden
/// outside this class and the resolver's <c>LockedEntityType</c>
/// property; enforcement is by code review until misuse appears.
/// </para>
/// <para>
/// Phase 2 (ADR 0008 C7) ships only <see cref="Document"/> and
/// <see cref="DocumentRevision"/>. Future phases add entries here as
/// new entity types are surfaced in the inspector (Asset / Job /
/// Operator / etc.). The list is non-exhaustive; the catalog grows
/// per-phase the same way <see cref="PermissionNames"/> does.
/// </para>
/// </remarks>
public static class LockedEntityTypes
{
    /// <summary>
    /// Canonical type string for <see cref="EasySynQ.Domain.Entities.Documents.Document"/>
    /// lockouts (Phase 2 — primary case is retirement per SPEC §5.1).
    /// </summary>
    public const string Document = "Document";

    /// <summary>
    /// Canonical type string for <see cref="EasySynQ.Domain.Entities.Documents.DocumentRevision"/>
    /// lockouts (Phase 2 — covers InReview, Approved, Superseded,
    /// Archived, soft-deleted; Draft is not a lockout).
    /// </summary>
    public const string DocumentRevision = "DocumentRevision";
}
