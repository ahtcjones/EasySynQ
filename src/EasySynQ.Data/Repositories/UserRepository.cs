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

    /// <inheritdoc />
    public async Task<IReadOnlyList<User>> GetUsersWithPermissionAsync(
        string permissionName,
        DateTime asOfUtc,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permissionName);

        if (asOfUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "asOfUtc must have DateTimeKind.Utc.",
                nameof(asOfUtc));
        }

        // Inverse of PermissionRepository.GetEffectivePermissionNamesForUserAsync:
        // start from the named Permission, project up through the two
        // link tables (each filtered by EffectivePeriod at asOfUtc),
        // union the candidate userIds. Two-round-trip shape rather
        // than a single composed query because EF Core 10's
        // IgnoreQueryFilters propagates across composed subqueries —
        // if we built the candidate set as a subquery of
        // Query().Where(Contains(...)), the outer User table's
        // soft-delete filter would be lifted alongside the link-
        // tables' filters. Materializing the candidates first keeps
        // the User-side soft-delete filter intact.
        //
        // IgnoreQueryFilters lifts the DbContext's as-of filter
        // (per ADR 0005, that filter reads the temporal resolver, not
        // the caller-supplied asOfUtc); soft-delete predicates are
        // re-applied explicitly on UserRole, RolePermission,
        // UserPermission, and Permission. Users themselves go through
        // Query() at the end so the standard soft-delete filter
        // remains in effect for them — soft-deleted users never
        // surface to a reviewer picker.

        var roleBranchUserIds =
            from ur in Context.UserRoles.IgnoreQueryFilters()
            where !ur.IsDeleted
               && ur.EffectivePeriod.EffectiveFromUtc <= asOfUtc
               && (ur.EffectivePeriod.EffectiveToUtc == null
                   || asOfUtc < ur.EffectivePeriod.EffectiveToUtc)
            join rp in Context.RolePermissions.IgnoreQueryFilters()
                on ur.RoleId equals rp.RoleId
            where !rp.IsDeleted
               && rp.EffectivePeriod.EffectiveFromUtc <= asOfUtc
               && (rp.EffectivePeriod.EffectiveToUtc == null
                   || asOfUtc < rp.EffectivePeriod.EffectiveToUtc)
            join p in Context.Permissions.IgnoreQueryFilters()
                on rp.PermissionId equals p.Id
            where !p.IsDeleted && p.Name == permissionName
            select ur.UserId;

        var directBranchUserIds =
            from up in Context.UserPermissions.IgnoreQueryFilters()
            where !up.IsDeleted
               && up.EffectivePeriod.EffectiveFromUtc <= asOfUtc
               && (up.EffectivePeriod.EffectiveToUtc == null
                   || asOfUtc < up.EffectivePeriod.EffectiveToUtc)
            join p in Context.Permissions.IgnoreQueryFilters()
                on up.PermissionId equals p.Id
            where !p.IsDeleted && p.Name == permissionName
            select up.UserId;

        var candidateUserIds = await roleBranchUserIds
            .Union(directBranchUserIds)
            .ToListAsync(cancellationToken);

        if (candidateUserIds.Count == 0)
        {
            return [];
        }

        return await Query()
            .Where(u => candidateUserIds.Contains(u.Id))
            .ToListAsync(cancellationToken);
    }
}
