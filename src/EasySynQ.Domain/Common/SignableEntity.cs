namespace EasySynQ.Domain.Common;

/// <summary>
/// Base for compliance-critical entities that may carry digital signatures or
/// be referenced by signed records, and which therefore transition from the
/// draft (hard-deletable) state to the immutable (soft-delete-only) state
/// once they cross the boundary described in SPEC §3.5.
/// </summary>
/// <remarks>
/// Extends <see cref="AuditableEntity"/> by adding <see cref="LockedAtUtc"/>,
/// the UTC instant at which the entity first became immutable. The transition
/// is one-way; once <see cref="LockedAtUtc"/> is set, the entity may not be
/// hard-deleted (only soft-deleted with reason).
/// <para>
/// Examples of signable entities: <c>Job</c>, <c>NCR</c>, <c>CAPA</c>,
/// <c>DocumentRevision</c>, <c>AuditFinding</c>, <c>RiskReview</c>.
/// Non-signable but audited entities (for example, <c>User</c> and
/// <c>Role</c>) derive from <see cref="AuditableEntity"/> directly.
/// </para>
/// </remarks>
public abstract class SignableEntity : AuditableEntity
{
    /// <summary>
    /// UTC instant at which this entity first transitioned from the draft
    /// state to the locked (immutable) state, or <see langword="null"/>
    /// while the entity is still in the draft state. Once set to a non-null
    /// value, this property is never reset to <see langword="null"/>.
    /// </summary>
    public DateTime? LockedAtUtc { get; protected set; }
}
