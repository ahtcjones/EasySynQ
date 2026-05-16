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
}
