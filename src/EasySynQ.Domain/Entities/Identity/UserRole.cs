using EasySynQ.Domain.Common;
using EasySynQ.Domain.ValueObjects;

namespace EasySynQ.Domain.Entities.Identity;

/// <summary>
/// Effective-dated assignment of a <see cref="Role"/> to a
/// <see cref="User"/>.
/// </summary>
/// <remarks>
/// <para>
/// A user may hold the same role for multiple non-overlapping periods,
/// or transition between roles over time. Querying "what role did this
/// user hold at the time of the event?" uses the
/// <see cref="IEffectiveDated"/> contract. Signature records capture
/// the role-at-time-of-sign as a string snapshot
/// (<c>Signature.RoleAtTimeOfSign</c>); the <see cref="UserRole"/>
/// table is the source of truth for that snapshot.
/// </para>
/// <para>
/// Per SPEC §3.7 (Rev 3.2), <see cref="EffectivePeriod"/>'s
/// <c>EffectiveFromUtc</c> may be in the future. A pre-scheduled role
/// transition — for example, "M. Rodriguez becomes Quality Manager
/// next Monday" — is a deliberately supported workflow. The
/// effective-dating semantics handle this naturally: queries with an
/// <c>asOf</c> before <c>EffectiveFromUtc</c> simply do not return the
/// not-yet-active assignment.
/// </para>
/// </remarks>
public class UserRole : AuditableEntity, IEffectiveDated
{
    /// <summary>Unique entity identifier.</summary>
    public Guid Id { get; protected set; }

    /// <summary>Identifier of the user holding the role.</summary>
    public Guid UserId { get; protected set; }

    /// <summary>Identifier of the role held.</summary>
    public Guid RoleId { get; protected set; }

    /// <summary>The effective period over which the assignment is active.</summary>
    public EffectiveDateRange EffectivePeriod { get; protected set; } = null!;

    /// <summary>
    /// Returns <see langword="true"/> when this assignment is active at
    /// the supplied UTC instant. Delegates to
    /// <see cref="EffectivePeriod"/>'s
    /// <see cref="EffectiveDateRange.IsInEffectAt(DateTime)"/>.
    /// </summary>
    /// <remarks>
    /// Explicitly implemented (rather than relying on the default
    /// interface method on <see cref="IEffectiveDated"/>) so callers can
    /// invoke <c>userRole.IsInEffectAt(...)</c> without first casting to
    /// the interface — C# default interface methods are otherwise only
    /// reachable through the interface reference.
    /// </remarks>
    /// <param name="utc">The UTC instant to evaluate.</param>
    /// <returns><see langword="true"/> if the assignment is in effect at
    /// <paramref name="utc"/>.</returns>
    public bool IsInEffectAt(DateTime utc) => EffectivePeriod.IsInEffectAt(utc);

    /// <summary>
    /// Parameterless constructor for the persistence layer. Do not call
    /// from application code.
    /// </summary>
    protected UserRole()
    {
    }

    /// <summary>
    /// Constructs a new user-role assignment.
    /// </summary>
    /// <param name="id">Unique entity identifier. Must not be
    /// <see cref="Guid.Empty"/>.</param>
    /// <param name="userId">Identifier of the user holding the role.
    /// Must not be <see cref="Guid.Empty"/>.</param>
    /// <param name="roleId">Identifier of the role held. Must not be
    /// <see cref="Guid.Empty"/>.</param>
    /// <param name="effectivePeriod">The effective period over which
    /// the assignment is active. Must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentException">Thrown when any
    /// <see cref="Guid"/> argument is <see cref="Guid.Empty"/>.</exception>
    /// <exception cref="ArgumentNullException">Thrown when
    /// <paramref name="effectivePeriod"/> is <see langword="null"/>.</exception>
    public UserRole(Guid id, Guid userId, Guid roleId, EffectiveDateRange effectivePeriod)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Id must not be Guid.Empty.", nameof(id));
        }
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("UserId must not be Guid.Empty.", nameof(userId));
        }
        if (roleId == Guid.Empty)
        {
            throw new ArgumentException("RoleId must not be Guid.Empty.", nameof(roleId));
        }

        ArgumentNullException.ThrowIfNull(effectivePeriod);

        Id = id;
        UserId = userId;
        RoleId = roleId;
        EffectivePeriod = effectivePeriod;
    }
}
