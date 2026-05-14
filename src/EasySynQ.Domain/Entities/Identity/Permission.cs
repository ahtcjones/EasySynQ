using System.Diagnostics.CodeAnalysis;

using EasySynQ.Domain.Common;

namespace EasySynQ.Domain.Entities.Identity;

/// <summary>
/// A named authorization permission (SPEC §3.4 — permission-based
/// authorization model, ADR 0007). A permission represents a single
/// capability check at a call site — for example, <c>"Document.Approve"</c>
/// — and is granted to users transitively via role bundles
/// (<see cref="RolePermission"/>) or directly
/// (<see cref="UserPermission"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Catalog discipline.</b> Permission names are code identifiers, not
/// user-editable data. A permission exists if and only if there is a code
/// path that checks for it — see
/// <see cref="EasySynQ.Domain.PermissionNames"/> for the canonical
/// constants. The Phase 1 migration seeds eleven system permissions; each
/// subsequent phase adds its own permissions in its own migration. A
/// <see cref="Permission"/> row with no code reference is dead data; a
/// code path that checks a permission name not in the catalog is a bug.
/// Coupling the catalog to migrations enforces the relationship at the
/// version-control level.
/// </para>
/// <para>
/// <b>Naming convention.</b> The <c>Resource.Action</c> form
/// (<c>"Document.Approve"</c>, <c>"Role.Create"</c>) is enforced by code
/// review, not by the domain. <see cref="Name"/> is unique and matches the
/// canonical string used at every call site.
/// </para>
/// </remarks>
[SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "ADR 0007 mandates the type name 'Permission' for the authorization domain entity. The CA1711 'Permission' suffix discouragement targets legacy CAS types; this is a domain entity in a non-security namespace and the name is the spec-prescribed identifier.")]
public class Permission : AuditableEntity
{
    /// <summary>Unique entity identifier.</summary>
    public Guid Id { get; protected set; }

    /// <summary>
    /// Canonical permission name in <c>Resource.Action</c> form (for
    /// example, <c>"Document.Approve"</c>). Unique across all permissions.
    /// </summary>
    public string Name { get; protected set; } = string.Empty;

    /// <summary>Human-readable description of the permission's purpose.</summary>
    public string Description { get; protected set; } = string.Empty;

    /// <summary>
    /// Grouping label for UI rendering (for example, <c>"System"</c>,
    /// <c>"Document"</c>, <c>"Production"</c>). Multiple permissions may
    /// share a category.
    /// </summary>
    public string Category { get; protected set; } = string.Empty;

    /// <summary>
    /// Parameterless constructor for the persistence layer. Do not call
    /// from application code.
    /// </summary>
    protected Permission()
    {
    }

    /// <summary>
    /// Constructs a new permission with the supplied identity, name,
    /// description, and category.
    /// </summary>
    /// <param name="id">Unique entity identifier. Must not be
    /// <see cref="Guid.Empty"/>.</param>
    /// <param name="name">Canonical permission name. Must not be
    /// <see langword="null"/>, empty, or whitespace.</param>
    /// <param name="description">Human-readable description. Must not be
    /// <see langword="null"/>, empty, or whitespace.</param>
    /// <param name="category">Grouping label. Must not be
    /// <see langword="null"/>, empty, or whitespace.</param>
    /// <exception cref="ArgumentException">Thrown when any input fails
    /// validation.</exception>
    public Permission(Guid id, string name, string description, string category)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Id must not be Guid.Empty.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(category);

        Id = id;
        Name = name;
        Description = description;
        Category = category;
    }
}
