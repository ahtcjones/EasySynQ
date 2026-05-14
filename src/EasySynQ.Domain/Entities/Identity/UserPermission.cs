using System.Diagnostics.CodeAnalysis;

using EasySynQ.Domain.Common;
using EasySynQ.Domain.ValueObjects;

namespace EasySynQ.Domain.Entities.Identity;

/// <summary>
/// Effective-dated direct grant of a <see cref="Permission"/> to a
/// <see cref="User"/>, modeling the "trained and authorized for X outside
/// the user's default role" case (ADR 0007). A user's effective permission
/// set is the union of permissions reached transitively through role
/// membership plus permissions granted directly via this link table.
/// </summary>
/// <remarks>
/// <para>
/// <b>Additive only.</b> Per-user grants only add to the user's
/// authorities; they cannot subtract a permission that a role would
/// otherwise grant. Subtractive (denied) permissions are an unmodeled case
/// for Phase 1 — see ADR 0007 §Alternatives Considered.
/// </para>
/// <para>
/// Mirrors <see cref="RolePermission"/>'s shape with <c>UserId</c> in
/// place of <c>RoleId</c>.
/// </para>
/// </remarks>
[SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "ADR 0007 mandates the type name 'UserPermission' for the user→permission direct-grant entity. CA1711's 'Permission' suffix discouragement targets legacy CAS types; this is a domain link entity.")]
public class UserPermission : AuditableEntity, IEffectiveDated
{
    /// <summary>Unique entity identifier.</summary>
    public Guid Id { get; protected set; }

    /// <summary>Identifier of the user receiving the permission.</summary>
    public Guid UserId { get; protected set; }

    /// <summary>Identifier of the permission granted.</summary>
    public Guid PermissionId { get; protected set; }

    /// <summary>The effective period over which the grant is active.</summary>
    public EffectiveDateRange EffectivePeriod { get; protected set; } = null!;

    /// <summary>
    /// Navigation to the <see cref="User"/> receiving the permission.
    /// Populated by EF Core when the <see cref="UserPermission"/> is
    /// loaded with <c>.Include(up => up.User)</c>; otherwise default
    /// <see langword="null"/>.
    /// </summary>
    public User User { get; protected set; } = null!;

    /// <summary>
    /// Navigation to the <see cref="Permission"/> granted. Populated by
    /// EF Core when the <see cref="UserPermission"/> is loaded with
    /// <c>.Include(up => up.Permission)</c>; otherwise default
    /// <see langword="null"/>.
    /// </summary>
    public Permission Permission { get; protected set; } = null!;

    /// <summary>
    /// Returns <see langword="true"/> when this grant is active at the
    /// supplied UTC instant. Delegates to
    /// <see cref="EffectiveDateRange.IsInEffectAt(DateTime)"/>.
    /// </summary>
    /// <param name="utc">The UTC instant to evaluate.</param>
    /// <returns><see langword="true"/> if the grant is in effect at
    /// <paramref name="utc"/>.</returns>
    public bool IsInEffectAt(DateTime utc) => EffectivePeriod.IsInEffectAt(utc);

    /// <summary>
    /// Parameterless constructor for the persistence layer. Do not call
    /// from application code.
    /// </summary>
    protected UserPermission()
    {
    }

    /// <summary>
    /// Constructs a new user-permission direct grant.
    /// </summary>
    /// <param name="id">Unique entity identifier. Must not be
    /// <see cref="Guid.Empty"/>.</param>
    /// <param name="userId">Identifier of the user receiving the
    /// permission. Must not be <see cref="Guid.Empty"/>.</param>
    /// <param name="permissionId">Identifier of the permission granted.
    /// Must not be <see cref="Guid.Empty"/>.</param>
    /// <param name="effectivePeriod">The effective period over which the
    /// grant is active. Must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentException">Thrown when any
    /// <see cref="Guid"/> argument is <see cref="Guid.Empty"/>.</exception>
    /// <exception cref="ArgumentNullException">Thrown when
    /// <paramref name="effectivePeriod"/> is <see langword="null"/>.</exception>
    public UserPermission(Guid id, Guid userId, Guid permissionId, EffectiveDateRange effectivePeriod)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Id must not be Guid.Empty.", nameof(id));
        }
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("UserId must not be Guid.Empty.", nameof(userId));
        }
        if (permissionId == Guid.Empty)
        {
            throw new ArgumentException("PermissionId must not be Guid.Empty.", nameof(permissionId));
        }

        ArgumentNullException.ThrowIfNull(effectivePeriod);

        Id = id;
        UserId = userId;
        PermissionId = permissionId;
        EffectivePeriod = effectivePeriod;
    }
}
