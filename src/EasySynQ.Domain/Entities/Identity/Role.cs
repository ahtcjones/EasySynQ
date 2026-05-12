using EasySynQ.Domain.Common;

namespace EasySynQ.Domain.Entities.Identity;

/// <summary>
/// Named role used in role-based authorization (SPEC §3.4). Operator,
/// Lab Tech, Quality Manager, Auditor (read-only), and Administrator are
/// the minimum set; additional roles may be added per module
/// (for example, Maintenance Tech for the Assets module).
/// </summary>
/// <remarks>
/// Roles are immutable by policy: once a role is created, its
/// <see cref="Name"/> should not change because role assignments
/// (<see cref="UserRole"/>) and signatures (<c>RoleAtTimeOfSign</c>)
/// refer to roles by name-at-time-of-reference. The domain models the
/// data shape but does not enforce immutability at the entity level —
/// the constraint is enforced by the seed-data discipline and admin UI.
/// </remarks>
public class Role : AuditableEntity
{
    /// <summary>Unique entity identifier.</summary>
    public Guid Id { get; protected set; }

    /// <summary>
    /// Canonical role name (for example, <c>"QualityManager"</c>,
    /// <c>"LabTech"</c>).
    /// </summary>
    public string Name { get; protected set; } = string.Empty;

    /// <summary>Human-readable description of the role's purpose.</summary>
    public string Description { get; protected set; } = string.Empty;

    /// <summary>
    /// Parameterless constructor for the persistence layer. Do not call
    /// from application code.
    /// </summary>
    protected Role()
    {
    }

    /// <summary>
    /// Constructs a new role with the supplied name and description.
    /// </summary>
    /// <param name="id">Unique entity identifier. Must not be
    /// <see cref="Guid.Empty"/>.</param>
    /// <param name="name">Canonical role name. Must not be
    /// <see langword="null"/>, empty, or whitespace.</param>
    /// <param name="description">Human-readable description. Must not
    /// be <see langword="null"/>, empty, or whitespace.</param>
    /// <exception cref="ArgumentException">Thrown when any input fails
    /// validation.</exception>
    public Role(Guid id, string name, string description)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Id must not be Guid.Empty.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        Id = id;
        Name = name;
        Description = description;
    }
}
