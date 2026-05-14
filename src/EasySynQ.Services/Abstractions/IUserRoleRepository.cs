using EasySynQ.Domain.Entities.Identity;

namespace EasySynQ.Services.Abstractions;

/// <summary>
/// UserRole-specific repository. Inherits the generic
/// <see cref="IRepository{TEntity, TId}"/> surface and adds the
/// role-name resolution method consumed by the auth service when
/// assembling the signed-in user's session snapshot (ADR 0007).
/// </summary>
public interface IUserRoleRepository : IRepository<UserRole, Guid>
{
    /// <summary>
    /// Returns the names of every <see cref="Role"/> the user holds at
    /// <paramref name="asOfUtc"/>.
    /// </summary>
    /// <remarks>
    /// Filters <see cref="UserRole"/> rows by effective period at
    /// <paramref name="asOfUtc"/> using an explicit predicate that
    /// bypasses the DbContext's "as-of" query filter. Joins to
    /// <see cref="Role"/> for the names. Soft-deleted roles or
    /// user-role assignments are excluded by the standard
    /// <c>!IsDeleted</c> filter; <see cref="IgnoreQueryFilters"/> is
    /// scoped to lift only the effective-dating filter.
    /// </remarks>
    /// <param name="userId">Identifier of the user whose role set is
    /// being resolved.</param>
    /// <param name="asOfUtc">UTC instant at which to evaluate effective
    /// dating. Must be of <see cref="DateTimeKind.Utc"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The list of role names the user holds at the supplied
    /// instant; possibly empty.</returns>
    /// <exception cref="ArgumentException">Thrown when
    /// <paramref name="asOfUtc"/> is not of
    /// <see cref="DateTimeKind.Utc"/>.</exception>
    Task<IReadOnlyList<string>> GetEffectiveRoleNamesAsync(
        Guid userId,
        DateTime asOfUtc,
        CancellationToken cancellationToken);
}
