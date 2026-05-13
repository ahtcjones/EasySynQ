namespace EasySynQ.Services.Abstractions;

/// <summary>
/// Generic repository contract for compliance-critical entities. The
/// <typeparamref name="TEntity"/> type is unconstrained beyond <c>class</c>
/// so the same contract serves both <c>AuditableEntity</c>-derived types
/// (User, Role, UserRole, Signature, LockReason, Snapshot) and the
/// <c>AuditLogEntry</c> append-only table.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exposes <see cref="IQueryable{TEntity}"/>:</b> the
/// hide-everything-behind-named-methods style of the classical repository
/// pattern is unworkable for compliance queries. Lock-chain traversal,
/// audit-log slicing, "what was the FPY by part-master rev during this
/// month?" — every meaningful read involves an ad-hoc projection, filter,
/// or join that the repository cannot enumerate in advance without
/// becoming a god-class.
/// </para>
/// <para>
/// <see cref="Query"/> returns a composable <see cref="IQueryable{TEntity}"/>
/// so callers can build the exact shape they need. The trade-off is that
/// callers must be aware of LINQ-to-EF translation limits — expressions
/// the provider cannot translate fall back to client evaluation (or
/// throw, depending on EF Core 10's defaults). Repository methods that
/// MUTATE state (<see cref="AddAsync"/>, <see cref="SoftDelete"/>,
/// <see cref="HardDelete"/>) are the chokepoint where invariants are
/// enforced — e.g., the audit log's append-only rule.
/// </para>
/// <para>
/// <b>Filters:</b> the standard query methods (<see cref="GetByIdAsync"/>,
/// <see cref="Query"/>) honor the soft-delete global filter and (for
/// <see cref="EasySynQ.Domain.Common.IEffectiveDated"/> entities) the
/// effective-dating filter. The <c>IncludingDeleted</c> variants opt out
/// of the soft-delete filter for admin / recovery / forensic paths.
/// </para>
/// </remarks>
public interface IRepository<TEntity, TId>
    where TEntity : class
{
    /// <summary>
    /// Returns the entity with the supplied id, or <see langword="null"/>
    /// if not found or soft-deleted.
    /// </summary>
    Task<TEntity?> GetByIdAsync(TId id, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the entity with the supplied id even if it has been
    /// soft-deleted, or <see langword="null"/> if no row matches at all.
    /// For admin / recovery / forensic scenarios; the regular
    /// <see cref="GetByIdAsync"/> is the right default for normal flows.
    /// </summary>
    Task<TEntity?> GetByIdIncludingDeletedAsync(TId id, CancellationToken cancellationToken);

    /// <summary>
    /// Returns <see langword="true"/> when at least one non-soft-deleted
    /// row exists for this entity. Provided as a first-class repository
    /// method so service-layer callers don't have to reach for EF Core's
    /// <c>AnyAsync</c> extension on <see cref="Query"/> — the Services
    /// project does not reference <c>Microsoft.EntityFrameworkCore</c>
    /// (per ADR 0003's dependency direction).
    /// </summary>
    Task<bool> AnyAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Returns a composable query surface for this entity. The query
    /// honors the soft-delete filter and any global query filters
    /// configured on the entity. Callers extend it with LINQ operators.
    /// </summary>
    IQueryable<TEntity> Query();

    /// <summary>
    /// Same as <see cref="Query"/> but bypasses the soft-delete filter.
    /// Effective-dating filters (if any) still apply.
    /// </summary>
    IQueryable<TEntity> QueryIncludingDeleted();

    /// <summary>
    /// Adds <paramref name="entity"/> to the unit-of-work; the row is not
    /// persisted until <see cref="SaveChangesAsync"/> runs.
    /// </summary>
    Task AddAsync(TEntity entity, CancellationToken cancellationToken);

    /// <summary>
    /// Persists pending changes. Equivalent to
    /// <c>DbContext.SaveChangesAsync</c> — the interceptor pipeline
    /// (standard-fields + audit) fires here.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Soft-deletes the entity by setting <c>IsDeleted = true</c>. Throws
    /// <see cref="InvalidOperationException"/> if the entity type does
    /// not inherit <c>AuditableEntity</c> (e.g.,
    /// <c>AuditLogEntry</c> has no IsDeleted column per SPEC §3.4).
    /// </summary>
    void SoftDelete(TEntity entity);

    /// <summary>
    /// Hard-deletes the entity by removing the row. The service layer is
    /// responsible for enforcing the SPEC §3.5 / ADR 0002 policy that
    /// hard-delete is only permitted on draft entities (not signed, not
    /// referenced by a signed record); the repository performs the
    /// operation but does not check those preconditions.
    /// </summary>
    void HardDelete(TEntity entity);
}
