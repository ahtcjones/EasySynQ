namespace EasySynQ.Services.LockReasons;

/// <summary>
/// Default <see cref="ILockReasonResolverRegistry"/> implementation
/// (ADR 0012). Composes from <c>IEnumerable&lt;ILockReasonResolver&gt;</c>
/// at construction; resolvers are keyed by their
/// <see cref="ILockReasonResolver.LockedEntityType"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Duplicate-key handling.</b> If two resolvers register the same
/// <see cref="ILockReasonResolver.LockedEntityType"/>, the constructor
/// throws <see cref="System.InvalidOperationException"/>. The condition
/// is unreachable in any documented DI configuration — each phase
/// registers exactly one resolver per type — and a silent
/// last-wins or first-wins would let a configuration bug propagate to
/// production unobserved.
/// </para>
/// </remarks>
public sealed class LockReasonResolverRegistry : ILockReasonResolverRegistry
{
    private readonly Dictionary<string, ILockReasonResolver> _byType;

    /// <summary>
    /// Constructs the registry from the registered resolvers. The DI
    /// container supplies the enumerable.
    /// </summary>
    /// <param name="resolvers">All registered
    /// <see cref="ILockReasonResolver"/> implementations.</param>
    /// <exception cref="System.ArgumentNullException">Thrown when
    /// <paramref name="resolvers"/> is <see langword="null"/>.</exception>
    /// <exception cref="System.InvalidOperationException">Thrown when
    /// two resolvers register the same
    /// <see cref="ILockReasonResolver.LockedEntityType"/>.</exception>
    public LockReasonResolverRegistry(IEnumerable<ILockReasonResolver> resolvers)
    {
        ArgumentNullException.ThrowIfNull(resolvers);

        _byType = new Dictionary<string, ILockReasonResolver>(StringComparer.Ordinal);
        foreach (var resolver in resolvers)
        {
            if (!_byType.TryAdd(resolver.LockedEntityType, resolver))
            {
                throw new InvalidOperationException(
                    $"Duplicate ILockReasonResolver registration for LockedEntityType " +
                    $"'{resolver.LockedEntityType}'. Each type must have exactly one " +
                    $"resolver — verify DI configuration.");
            }
        }
    }

    /// <inheritdoc />
    public ILockReasonResolver? GetResolver(string lockedEntityType)
    {
        if (string.IsNullOrWhiteSpace(lockedEntityType))
        {
            return null;
        }

        return _byType.TryGetValue(lockedEntityType, out var resolver)
            ? resolver
            : null;
    }
}
