using EasySynQ.Domain.Entities.Audit;

namespace EasySynQ.Services.Abstractions;

/// <summary>
/// Repository surface for <see cref="LockReason"/> rows (ADR 0012 C7a).
/// Adds the <c>(LockedEntityType, LockedEntityId)</c> lookup that the
/// lock-inspector view-model uses to check the cache before invoking
/// the resolver, plus the write-through cache path.
/// </summary>
/// <remarks>
/// <para>
/// The dedicated interface remains so future phases can add lock-reason
/// specific queries (e.g., "every active lock for entity type X" for an
/// admin overview) without disturbing the generic repository contract.
/// </para>
/// </remarks>
public interface ILockReasonRepository : IRepository<LockReason, Guid>
{
    /// <summary>
    /// Returns the cached <see cref="LockReason"/> for the supplied
    /// <c>(LockedEntityType, LockedEntityId)</c> pair, or
    /// <see langword="null"/> if no row has been cached yet. Soft-deleted
    /// rows are excluded by the standard soft-delete filter — a
    /// soft-deleted <c>LockReason</c> row represents a chain that has
    /// been administratively dropped (rare), and the inspector treats
    /// its absence the same as "no cache" and re-invokes the resolver.
    /// </summary>
    /// <remarks>
    /// Uses the existing index on
    /// <c>(LockedEntityType, LockedEntityId)</c> declared in
    /// <c>LockReasonConfiguration</c>.
    /// </remarks>
    /// <param name="lockedEntityType">Canonical type string (see
    /// <see cref="EasySynQ.Domain.LockedEntityTypes"/>). Must not be
    /// <see langword="null"/>, empty, or whitespace.</param>
    /// <param name="lockedEntityId">Canonical string-form identifier of
    /// the locked entity. Must not be <see langword="null"/>, empty, or
    /// whitespace.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The cached lock reason or <see langword="null"/>.</returns>
    Task<LockReason?> GetByLockedEntityAsync(
        string lockedEntityType,
        string lockedEntityId,
        CancellationToken cancellationToken);
}
