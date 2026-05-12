namespace EasySynQ.Domain.Common;

/// <summary>
/// Base for entities that require an audit trail (per SPEC §3.4) but do not
/// carry digital signatures themselves and are not subject to the
/// draft → immutable lifecycle transition described in SPEC §3.5.
/// </summary>
/// <remarks>
/// Use <see cref="AuditableEntity"/> for entities whose CRUD activity must
/// be audit-logged but which never carry a signature or appear in a
/// lock-chain — for example, user accounts and role definitions.
/// <para>
/// For entities that may carry signatures, be referenced by signed records,
/// or have a draft → immutable transition, derive from
/// <see cref="SignableEntity"/> instead. <see cref="SignableEntity"/>
/// extends this type by adding the <c>LockedAtUtc</c> field that records
/// the moment of transition.
/// </para>
/// </remarks>
public abstract class AuditableEntity
{
    /// <summary>
    /// User identifier of the creator. Populated by the persistence layer's
    /// standard-fields interceptor at insert time. Empty string until the
    /// entity is first persisted.
    /// </summary>
    public string CreatedBy { get; protected set; } = string.Empty;

    /// <summary>UTC instant of creation.</summary>
    public DateTime CreatedUtc { get; protected set; }

    /// <summary>User identifier of the most recent modifier.</summary>
    public string ModifiedBy { get; protected set; } = string.Empty;

    /// <summary>UTC instant of the most recent modification.</summary>
    public DateTime ModifiedUtc { get; protected set; }

    /// <summary>
    /// Optimistic-concurrency token. Updated by the persistence layer on
    /// every write. The exact byte layout is an implementation concern of
    /// the data layer; in <c>EasySynQ.Data</c> this maps to a counter
    /// incremented by the SaveChanges interceptor (SQLite has no native
    /// rowversion type).
    /// </summary>
    public byte[] RowVersion { get; protected set; } = [];

    /// <summary>
    /// True when the entity has been soft-deleted. Soft-deleted entities
    /// remain in the operational store for retention and remain queryable
    /// through historical / audit paths but are filtered from active list
    /// views (SPEC §3.5).
    /// </summary>
    public bool IsDeleted { get; protected set; }
}
