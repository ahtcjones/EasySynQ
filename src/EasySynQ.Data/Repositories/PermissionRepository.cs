using EasySynQ.Data.Context;
using EasySynQ.Domain.Entities.Identity;
using EasySynQ.Services.Abstractions;

using Microsoft.EntityFrameworkCore;

namespace EasySynQ.Data.Repositories;

/// <summary>
/// Permission-specific repository. Inherits the generic
/// <see cref="Repository{TEntity, TId}"/> surface and adds the
/// effective-permission-resolution method described in ADR 0007.
/// </summary>
public class PermissionRepository : Repository<Permission, Guid>, IPermissionRepository
{
    /// <summary>Constructs the repository over the supplied DbContext.</summary>
    public PermissionRepository(EasySynQDbContext context) : base(context)
    {
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetEffectivePermissionNamesForUserAsync(
        Guid userId,
        DateTime asOfUtc,
        CancellationToken cancellationToken)
    {
        if (asOfUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "asOfUtc must have DateTimeKind.Utc.",
                nameof(asOfUtc));
        }

        // Both branches use IgnoreQueryFilters() to lift the DbContext's
        // global effective-dating filter (which reads from the temporal
        // resolver, not the caller-supplied asOfUtc per ADR 0005). The
        // soft-delete predicates are re-applied explicitly so soft-deleted
        // rows in any of the four involved tables (UserRole,
        // RolePermission, UserPermission, Permission) are excluded.

        var rolePath =
            from ur in Context.UserRoles.IgnoreQueryFilters()
            where !ur.IsDeleted
               && ur.UserId == userId
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
            where !p.IsDeleted
            select p.Name;

        var directPath =
            from up in Context.UserPermissions.IgnoreQueryFilters()
            where !up.IsDeleted
               && up.UserId == userId
               && up.EffectivePeriod.EffectiveFromUtc <= asOfUtc
               && (up.EffectivePeriod.EffectiveToUtc == null
                   || asOfUtc < up.EffectivePeriod.EffectiveToUtc)
            join p in Context.Permissions.IgnoreQueryFilters()
                on up.PermissionId equals p.Id
            where !p.IsDeleted
            select p.Name;

        // Union already deduplicates server-side — no Distinct() needed.
        return await rolePath.Union(directPath).ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Permission>> GetByNamesAsync(
        IReadOnlyCollection<string> names,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(names);

        if (names.Count == 0)
        {
            return [];
        }

        // The soft-delete filter on Permissions is honored implicitly
        // (no IgnoreQueryFilters here) — soft-deleted permission rows
        // would not be in the active catalog and shouldn't surface to
        // a "lookup by name" caller. Contains over a small known set
        // translates to a SQL IN clause.
        return await Context.Permissions
            .Where(p => names.Contains(p.Name))
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, IReadOnlyCollection<string>>>
        GetEffectiveRolePermissionMapForUserAsync(
            Guid userId,
            DateTime asOfUtc,
            CancellationToken cancellationToken)
    {
        if (asOfUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "asOfUtc must have DateTimeKind.Utc.",
                nameof(asOfUtc));
        }

        // Same shape as GetEffectivePermissionNamesForUserAsync's
        // role-path branch, but projects to the (RoleName, PermissionName)
        // pair so we can group by role on the client. IgnoreQueryFilters
        // lifts the global as-of filter (ADR 0005) so the explicit
        // EffectivePeriod predicates can use the caller's asOfUtc; soft-
        // delete predicates re-applied per the same rationale.
        //
        // Direct UserPermission grants are deliberately NOT included —
        // ADR 0009's signature dialog covers role-derived permissions
        // only, with the corner case (direct-grant-only path to a
        // gating permission) documented in ICurrentUserAccessor's
        // RolePermissions remarks.

        var roleAndPermissionPairs =
            from ur in Context.UserRoles.IgnoreQueryFilters()
            where !ur.IsDeleted
               && ur.UserId == userId
               && ur.EffectivePeriod.EffectiveFromUtc <= asOfUtc
               && (ur.EffectivePeriod.EffectiveToUtc == null
                   || asOfUtc < ur.EffectivePeriod.EffectiveToUtc)
            join role in Context.Roles.IgnoreQueryFilters()
                on ur.RoleId equals role.Id
            where !role.IsDeleted
            join rp in Context.RolePermissions.IgnoreQueryFilters()
                on role.Id equals rp.RoleId
            where !rp.IsDeleted
               && rp.EffectivePeriod.EffectiveFromUtc <= asOfUtc
               && (rp.EffectivePeriod.EffectiveToUtc == null
                   || asOfUtc < rp.EffectivePeriod.EffectiveToUtc)
            join p in Context.Permissions.IgnoreQueryFilters()
                on rp.PermissionId equals p.Id
            where !p.IsDeleted
            select new { RoleName = role.Name, PermissionName = p.Name };

        var pairs = await roleAndPermissionPairs.ToListAsync(cancellationToken);

        // Group client-side. Result is { roleName -> distinct permission names }.
        return pairs
            .GroupBy(x => x.RoleName, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyCollection<string>)g.Select(x => x.PermissionName).Distinct(StringComparer.Ordinal).ToList(),
                StringComparer.Ordinal);
    }
}
