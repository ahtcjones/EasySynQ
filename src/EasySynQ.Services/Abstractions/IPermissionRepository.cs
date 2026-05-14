using EasySynQ.Domain.Entities.Identity;

namespace EasySynQ.Services.Abstractions;

/// <summary>
/// Permission-specific repository. Inherits the generic
/// <see cref="IRepository{TEntity, TId}"/> surface and adds the
/// permission-resolution method that combines role-derived and direct
/// per-user grants into a single deduplicated set (ADR 0007).
/// </summary>
public interface IPermissionRepository : IRepository<Permission, Guid>
{
    /// <summary>
    /// Returns the deduplicated names of every <see cref="Permission"/>
    /// that the user holds at <paramref name="asOfUtc"/>, via any
    /// combination of role membership (<see cref="UserRole"/> →
    /// <see cref="RolePermission"/>) and direct per-user grants
    /// (<see cref="UserPermission"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two link tables and the role-assignment table are each
    /// filtered by effective period at <paramref name="asOfUtc"/> using
    /// explicit predicates that bypass the DbContext's "as-of" query
    /// filter (per ADR 0005, the global filter reads the temporal
    /// resolver, not a caller-supplied instant). The two branches are
    /// unioned, projected to <see cref="Permission.Name"/>, and
    /// deduplicated server-side.
    /// </para>
    /// <para>
    /// Returns an empty list when the user has no in-effect roles and no
    /// in-effect direct grants. Never returns <see langword="null"/>.
    /// </para>
    /// </remarks>
    /// <param name="userId">Identifier of the user whose effective
    /// permission set is being resolved.</param>
    /// <param name="asOfUtc">UTC instant at which to evaluate the
    /// effective-dated rows. Must be of <see cref="DateTimeKind.Utc"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The deduplicated, alphabetically-orderable list of
    /// permission names; possibly empty.</returns>
    /// <exception cref="ArgumentException">Thrown when
    /// <paramref name="asOfUtc"/> is not of
    /// <see cref="DateTimeKind.Utc"/>.</exception>
    Task<IReadOnlyList<string>> GetEffectivePermissionNamesForUserAsync(
        Guid userId,
        DateTime asOfUtc,
        CancellationToken cancellationToken);
}
