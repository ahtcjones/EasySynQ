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

    /// <summary>
    /// Returns the <see cref="Permission"/> entities whose
    /// <see cref="Permission.Name"/> appears in <paramref name="names"/>.
    /// Honors the soft-delete filter (soft-deleted permission rows are
    /// excluded). Permissions present in <paramref name="names"/> but
    /// not in the catalog are simply absent from the result — the
    /// method does not throw on unknown names.
    /// </summary>
    /// <remarks>
    /// Primary Phase 1 consumer is the bootstrap path, which fetches
    /// the eleven seeded system permissions by
    /// <see cref="EasySynQ.Domain.PermissionNames"/>.<c>All</c> to
    /// construct the Administrator role's
    /// <see cref="RolePermission"/> link rows. The shape generalizes
    /// to any "lookup permissions by code-side constant name" need
    /// (admin UI permission picker, future grants, etc.).
    /// </remarks>
    /// <param name="names">Permission names to look up. May be empty;
    /// must not be <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matching <see cref="Permission"/> entities; possibly
    /// empty, never <see langword="null"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when
    /// <paramref name="names"/> is <see langword="null"/>.</exception>
    Task<IReadOnlyList<Permission>> GetByNamesAsync(
        IReadOnlyCollection<string> names,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns the per-role breakdown of the supplied user's effective
    /// role-derived permissions at <paramref name="asOfUtc"/> (ADR
    /// 0009). Keys are the role names the user holds at
    /// <paramref name="asOfUtc"/>; values are the in-effect permission
    /// names that role grants the user. Roles the user holds but which
    /// have no permissions at <paramref name="asOfUtc"/> appear in the
    /// dictionary with an empty value collection.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Single EF Core query joining <c>UserRole</c> → <c>Role</c> →
    /// <c>RolePermission</c> → <c>Permission</c>; both link tables
    /// filtered by their own <c>EffectivePeriod</c> at
    /// <paramref name="asOfUtc"/>. The result is grouped client-side
    /// into the dictionary shape.
    /// </para>
    /// <para>
    /// <b>Direct grants are not included.</b> Per-user permissions
    /// granted via <c>UserPermission</c> (without going through a
    /// role) are NOT in this map — they are folded into the flat
    /// <see cref="GetEffectivePermissionNamesForUserAsync"/> result
    /// but have no role to attach to. The signature dialog (ADR 0009)
    /// reads this map for role filtering; the corner case where a
    /// user's only path to a gating permission is a direct grant is
    /// documented in <c>ICurrentUserAccessor</c>'s
    /// <c>RolePermissions</c> remarks.
    /// </para>
    /// <para>
    /// Returns an empty (but non-null) dictionary when the user holds
    /// no in-effect roles.
    /// </para>
    /// </remarks>
    /// <param name="userId">Identifier of the user whose role-permission
    /// breakdown is being resolved.</param>
    /// <param name="asOfUtc">UTC instant at which to evaluate the
    /// effective-dated rows. Must be of <see cref="DateTimeKind.Utc"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The per-role breakdown; possibly empty.</returns>
    /// <exception cref="ArgumentException">Thrown when
    /// <paramref name="asOfUtc"/> is not of
    /// <see cref="DateTimeKind.Utc"/>.</exception>
    Task<IReadOnlyDictionary<string, IReadOnlyCollection<string>>>
        GetEffectiveRolePermissionMapForUserAsync(
            Guid userId,
            DateTime asOfUtc,
            CancellationToken cancellationToken);
}
