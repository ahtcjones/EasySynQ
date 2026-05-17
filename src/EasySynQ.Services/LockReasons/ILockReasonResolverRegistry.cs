namespace EasySynQ.Services.LockReasons;

/// <summary>
/// Dispatches <c>LockedEntityType</c> strings to the registered
/// <see cref="ILockReasonResolver"/> for that type (ADR 0012). Composes
/// from <c>IEnumerable&lt;ILockReasonResolver&gt;</c> at construction —
/// each phase registers its own resolver implementations into DI and
/// they auto-discover here.
/// </summary>
/// <remarks>
/// <para>
/// Single-implementation interface. The registry exists so the
/// inspector view-model (and any other caller resolving a chain by
/// type-string) does not have to reach into the DI container directly
/// or hard-code the per-type switch.
/// </para>
/// </remarks>
public interface ILockReasonResolverRegistry
{
    /// <summary>
    /// Returns the <see cref="ILockReasonResolver"/> registered for the
    /// supplied <paramref name="lockedEntityType"/>, or
    /// <see langword="null"/> if no resolver handles that type. The
    /// inspector treats a null resolver the same as a null chain
    /// (surfaces "not locked / no chain available"). A null is not an
    /// exceptional condition — it would be a programming error to call
    /// the inspector for an unsupported type, but the registry remains
    /// permissive to avoid forcing every caller into a try/catch.
    /// </summary>
    /// <param name="lockedEntityType">Canonical type string (see
    /// <see cref="EasySynQ.Domain.LockedEntityTypes"/>).</param>
    /// <returns>The registered resolver, or <see langword="null"/>.</returns>
    ILockReasonResolver? GetResolver(string lockedEntityType);
}
