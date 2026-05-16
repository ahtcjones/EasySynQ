using EasySynQ.Domain.Entities.Identity;

namespace EasySynQ.Services.Abstractions;

/// <summary>
/// User-specific repository extending the generic contract with lookup
/// methods that don't fit the generic shape (case-insensitive username
/// search, etc.).
/// </summary>
public interface IUserRepository : IRepository<User, Guid>
{
    /// <summary>
    /// Finds a user by username, case-insensitively. Honors the
    /// soft-delete filter — soft-deleted users return
    /// <see langword="null"/>.
    /// </summary>
    /// <param name="username">The username to look up. Must not be
    /// <see langword="null"/>, empty, or whitespace.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<User?> FindByUsernameAsync(string username, CancellationToken cancellationToken);

    /// <summary>
    /// Fetches multiple users by id in a single query. Returns only
    /// rows that exist and are not soft-deleted; missing or
    /// soft-deleted ids are silently dropped from the result. Order
    /// is not guaranteed; callers project to a dictionary keyed by
    /// <see cref="User.Id"/>. Backs the C6a Document list view's
    /// author-username resolution.
    /// </summary>
    /// <param name="ids">User ids to fetch. Duplicates are deduped.
    /// An empty input returns an empty result without querying.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Found, non-soft-deleted users for the supplied ids.</returns>
    Task<IReadOnlyList<User>> GetByIdsAsync(
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns active (non-soft-deleted) users who hold the named
    /// <see cref="EasySynQ.Domain.Entities.Identity.Permission"/> at
    /// <paramref name="asOfUtc"/> via any combination of role
    /// membership (<see cref="UserRole"/> →
    /// <see cref="RolePermission"/>) or direct per-user grants
    /// (<see cref="UserPermission"/>). The inverse query of
    /// <see cref="IPermissionRepository.GetEffectivePermissionNamesForUserAsync"/>
    /// — given a permission name (typically <c>Document.Review</c>),
    /// who is eligible? Backs the C6b reviewer-picker.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two link tables and the role-assignment table are each
    /// filtered by effective period at <paramref name="asOfUtc"/>
    /// using explicit predicates that bypass the DbContext's
    /// "as-of" query filter (per ADR 0005, the global filter reads
    /// the temporal resolver, not a caller-supplied instant). The
    /// two branches are unioned, distincted to user ids, and the
    /// matching <see cref="User"/> rows are returned (the standard
    /// soft-delete filter excludes deleted users).
    /// </para>
    /// <para>
    /// Returns an empty list when the permission name is not in the
    /// catalog, or when no active user holds the permission at
    /// <paramref name="asOfUtc"/>. Never returns
    /// <see langword="null"/>. Order is not guaranteed; the
    /// reviewer-picker VM sorts client-side.
    /// </para>
    /// </remarks>
    /// <param name="permissionName">Canonical permission name (e.g.,
    /// <c>"Document.Review"</c>). Must not be <see langword="null"/>,
    /// empty, or whitespace. Unknown names return an empty result.</param>
    /// <param name="asOfUtc">UTC instant at which to evaluate the
    /// effective-dated rows. Must be of
    /// <see cref="DateTimeKind.Utc"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Active users holding the permission at the supplied
    /// instant; possibly empty.</returns>
    /// <exception cref="ArgumentException">Thrown when
    /// <paramref name="permissionName"/> is null/empty/whitespace,
    /// or <paramref name="asOfUtc"/> is not of
    /// <see cref="DateTimeKind.Utc"/>.</exception>
    Task<IReadOnlyList<User>> GetUsersWithPermissionAsync(
        string permissionName,
        DateTime asOfUtc,
        CancellationToken cancellationToken);
}
