using EasySynQ.Data.Context;
using EasySynQ.Domain.Entities.Identity;
using EasySynQ.Services.Abstractions;

using Microsoft.EntityFrameworkCore;

namespace EasySynQ.Data.Repositories;

/// <summary>
/// UserRole-specific repository. Inherits the generic
/// <see cref="Repository{TEntity, TId}"/> surface and adds the
/// effective-role-name resolution method consumed by the auth service
/// when populating the signed-in user's session snapshot (ADR 0007).
/// </summary>
public class UserRoleRepository : Repository<UserRole, Guid>, IUserRoleRepository
{
    /// <summary>Constructs the repository over the supplied DbContext.</summary>
    public UserRoleRepository(EasySynQDbContext context) : base(context)
    {
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetEffectiveRoleNamesAsync(
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

        // IgnoreQueryFilters() lifts the DbContext's effective-dating
        // filter so the caller-supplied asOfUtc is the temporal axis.
        // Soft-delete is re-applied explicitly on both UserRole and Role.
        var query =
            from ur in Context.UserRoles.IgnoreQueryFilters()
            where !ur.IsDeleted
               && ur.UserId == userId
               && ur.EffectivePeriod.EffectiveFromUtc <= asOfUtc
               && (ur.EffectivePeriod.EffectiveToUtc == null
                   || asOfUtc < ur.EffectivePeriod.EffectiveToUtc)
            join r in Context.Roles.IgnoreQueryFilters()
                on ur.RoleId equals r.Id
            where !r.IsDeleted
            select r.Name;

        return await query.Distinct().ToListAsync(cancellationToken);
    }
}
