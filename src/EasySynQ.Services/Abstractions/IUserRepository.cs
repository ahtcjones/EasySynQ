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
}
