using EasySynQ.Domain.Entities.Audit;

namespace EasySynQ.Services.LockReasons;

/// <summary>
/// Produces a <see cref="LockReason"/> chain from live entity state for
/// one <c>LockedEntityType</c> (ADR 0012). Phase 2 has one
/// implementation (<c>DocumentLockReasonResolver</c>) registered twice —
/// once for <see cref="EasySynQ.Domain.LockedEntityTypes.Document"/>
/// and once for <see cref="EasySynQ.Domain.LockedEntityTypes.DocumentRevision"/>.
/// Future phases register their own implementations for Asset / Job /
/// Operator etc. via the same registry pattern.
/// </summary>
/// <remarks>
/// <para>
/// <b>Lazy resolution.</b> Per ADR 0012's decision, resolvers do not
/// cache or persist their results — that is the inspector
/// view-model's responsibility via the write-through cache. Resolvers
/// are pure: given the same live entity state, they return the same
/// chain. Side-effect-free.
/// </para>
/// <para>
/// <b>Not-locked is null, not throw.</b> When the entity exists but is
/// not currently in a locked state (e.g., a <c>DocumentRevision</c> in
/// <c>Draft</c>), the resolver returns <see langword="null"/>. The
/// inspector VM surfaces "not locked" to the user. Throwing here would
/// force callers into defensive try/catch around a non-exceptional case.
/// </para>
/// <para>
/// <b>Unknown id is null.</b> When the entity does not exist (or is
/// reachable only via the IncludingDeleted path and the chain template
/// has no soft-delete case for it), the resolver returns
/// <see langword="null"/>. Same reasoning — the inspector VM treats
/// "no chain" uniformly.
/// </para>
/// </remarks>
public interface ILockReasonResolver
{
    /// <summary>
    /// Canonical <c>LockedEntityType</c> string this resolver handles.
    /// Match one of the constants in
    /// <see cref="EasySynQ.Domain.LockedEntityTypes"/>. The
    /// <see cref="ILockReasonResolverRegistry"/> uses this to dispatch
    /// <c>(lockedEntityType, lockedEntityId)</c> lookups to the correct
    /// resolver.
    /// </summary>
    string LockedEntityType { get; }

    /// <summary>
    /// Constructs the lock-reason chain for the supplied
    /// <paramref name="lockedEntityId"/>, reading live entity state.
    /// Returns <see langword="null"/> when the entity is not currently
    /// locked or when the entity does not exist.
    /// </summary>
    /// <param name="lockedEntityId">String-form identifier of the
    /// entity whose lock state is being interrogated. The resolver
    /// validates the format internally (e.g., parses the string as a
    /// <see cref="System.Guid"/> when its entity type uses Guid
    /// primary keys).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A validated <see cref="LockReason"/> with a non-empty
    /// chain, or <see langword="null"/> when no lock applies.</returns>
    /// <exception cref="System.ArgumentException">Thrown when
    /// <paramref name="lockedEntityId"/> is null, empty, or
    /// whitespace.</exception>
    Task<LockReason?> ResolveAsync(
        string lockedEntityId,
        CancellationToken cancellationToken);
}
