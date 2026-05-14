using System.Diagnostics.CodeAnalysis;

using EasySynQ.Domain.Common;
using EasySynQ.Domain.ValueObjects;

namespace EasySynQ.Domain.Entities.Identity;

/// <summary>
/// Effective-dated link between a <see cref="Role"/> and a
/// <see cref="Permission"/>. A role's effective permission set at a given
/// UTC instant is the union of every <see cref="RolePermission"/> whose
/// <see cref="EffectivePeriod"/> contains that instant. Per ADR 0007 and
/// SPEC §3.7, effective dating is mandatory on this link table so
/// historical compliance questions ("what was Plant Manager authorized to
/// do on 2026-01-15?") are answerable from the schema alone.
/// </summary>
/// <remarks>
/// Mirrors <see cref="UserRole"/>'s shape. Per SPEC §3.7 (Rev 3.2),
/// <see cref="EffectivePeriod"/>'s <c>EffectiveFromUtc</c> may be in the
/// future — a pre-scheduled permission grant is a supported workflow.
/// </remarks>
[SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "ADR 0007 mandates the type name 'RolePermission' for the role→permission link entity. CA1711's 'Permission' suffix discouragement targets legacy CAS types; this is a domain link entity.")]
public class RolePermission : AuditableEntity, IEffectiveDated
{
    /// <summary>Unique entity identifier.</summary>
    public Guid Id { get; protected set; }

    /// <summary>Identifier of the role receiving the permission.</summary>
    public Guid RoleId { get; protected set; }

    /// <summary>Identifier of the permission granted.</summary>
    public Guid PermissionId { get; protected set; }

    /// <summary>The effective period over which the grant is active.</summary>
    public EffectiveDateRange EffectivePeriod { get; protected set; } = null!;

    /// <summary>
    /// Navigation to the <see cref="Role"/> receiving the permission.
    /// Populated by EF Core when the <see cref="RolePermission"/> is
    /// loaded with <c>.Include(rp => rp.Role)</c>; otherwise default
    /// <see langword="null"/> (despite the non-nullable type — the
    /// standard EF Core pattern for required navigations).
    /// </summary>
    public Role Role { get; protected set; } = null!;

    /// <summary>
    /// Navigation to the <see cref="Permission"/> granted. Populated by
    /// EF Core when the <see cref="RolePermission"/> is loaded with
    /// <c>.Include(rp => rp.Permission)</c>; otherwise default
    /// <see langword="null"/>.
    /// </summary>
    public Permission Permission { get; protected set; } = null!;

    /// <summary>
    /// Returns <see langword="true"/> when this grant is active at the
    /// supplied UTC instant. Delegates to
    /// <see cref="EffectiveDateRange.IsInEffectAt(DateTime)"/>.
    /// </summary>
    /// <remarks>
    /// Explicitly implemented (rather than relying on the default
    /// interface method on <see cref="IEffectiveDated"/>) so callers can
    /// invoke <c>rolePermission.IsInEffectAt(...)</c> without first
    /// casting to the interface — mirrors <see cref="UserRole"/>.
    /// </remarks>
    /// <param name="utc">The UTC instant to evaluate.</param>
    /// <returns><see langword="true"/> if the grant is in effect at
    /// <paramref name="utc"/>.</returns>
    public bool IsInEffectAt(DateTime utc) => EffectivePeriod.IsInEffectAt(utc);

    /// <summary>
    /// Parameterless constructor for the persistence layer. Do not call
    /// from application code.
    /// </summary>
    protected RolePermission()
    {
    }

    /// <summary>
    /// Constructs a new role-permission grant.
    /// </summary>
    /// <param name="id">Unique entity identifier. Must not be
    /// <see cref="Guid.Empty"/>.</param>
    /// <param name="roleId">Identifier of the role receiving the
    /// permission. Must not be <see cref="Guid.Empty"/>.</param>
    /// <param name="permissionId">Identifier of the permission granted.
    /// Must not be <see cref="Guid.Empty"/>.</param>
    /// <param name="effectivePeriod">The effective period over which the
    /// grant is active. Must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentException">Thrown when any
    /// <see cref="Guid"/> argument is <see cref="Guid.Empty"/>.</exception>
    /// <exception cref="ArgumentNullException">Thrown when
    /// <paramref name="effectivePeriod"/> is <see langword="null"/>.</exception>
    public RolePermission(Guid id, Guid roleId, Guid permissionId, EffectiveDateRange effectivePeriod)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Id must not be Guid.Empty.", nameof(id));
        }
        if (roleId == Guid.Empty)
        {
            throw new ArgumentException("RoleId must not be Guid.Empty.", nameof(roleId));
        }
        if (permissionId == Guid.Empty)
        {
            throw new ArgumentException("PermissionId must not be Guid.Empty.", nameof(permissionId));
        }

        ArgumentNullException.ThrowIfNull(effectivePeriod);

        Id = id;
        RoleId = roleId;
        PermissionId = permissionId;
        EffectivePeriod = effectivePeriod;
    }
}
