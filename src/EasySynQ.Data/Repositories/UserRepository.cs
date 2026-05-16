using EasySynQ.Data.Context;
using EasySynQ.Domain.Entities.Identity;
using EasySynQ.Services.Abstractions;

using Microsoft.EntityFrameworkCore;

namespace EasySynQ.Data.Repositories;

/// <summary>
/// User-specific repository. Inherits the generic
/// <see cref="Repository{TEntity, TId}"/> surface and adds the
/// username-lookup method.
/// </summary>
public class UserRepository : Repository<User, Guid>, IUserRepository
{
    /// <summary>Constructs the repository over the supplied DbContext.</summary>
    public UserRepository(EasySynQDbContext context) : base(context)
    {
    }

    /// <inheritdoc />
    public async Task<User?> FindByUsernameAsync(string username, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        // EF Core 10's SQLite provider does NOT translate
        // string.Equals(a, b, StringComparison.OrdinalIgnoreCase) — that
        // overload's translation is provider-unsupported even though the
        // CA1862 analyzer recommends it. EF.Functions.Collate is the
        // SQLite-aware idiom: emits "Username" COLLATE NOCASE = @username
        // at the SQL level, case-insensitive without locale dependence.
        return await Context.Users
            .FirstOrDefaultAsync(
                u => EF.Functions.Collate(u.Username, "NOCASE") == username,
                cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<User>> GetByIdsAsync(
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);

        // Dedupe + materialize once so the underlying enumerable
        // isn't iterated twice (the empty short-circuit + the query).
        var distinct = ids.Distinct().ToList();
        if (distinct.Count == 0)
        {
            return [];
        }

        return await Query()
            .Where(u => distinct.Contains(u.Id))
            .ToListAsync(cancellationToken);
    }
}
