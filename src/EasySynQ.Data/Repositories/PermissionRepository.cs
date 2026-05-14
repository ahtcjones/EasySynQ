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
}
