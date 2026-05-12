using EasySynQ.Domain.Common;

namespace EasySynQ.Domain.Entities.Audit;

/// <summary>
/// Causal chain explaining why a compliance-critical entity is in a
/// locked state. Per SPEC §4.3, every lock indicator must be
/// interrogable; this entity is the data behind that surface.
/// </summary>
/// <remarks>
/// A locked entity has at most one active <see cref="LockReason"/>.
/// Clearing the underlying cause (for example, closing the NCR that
/// held a job) deletes or supersedes the lock-reason record; while the
/// cause persists, the chain is the source of truth for the
/// lock-inspector UI.
/// <para>
/// Chain validation (enforced by the constructor):
/// <list type="bullet">
///   <item>At least one link.</item>
///   <item>Exactly one terminal link.</item>
///   <item>The terminal link is the last link in the chain.</item>
///   <item>Every non-terminal link has a non-null, non-whitespace
///   <see cref="LockReasonLink.Because"/> connector.</item>
/// </list>
/// </para>
/// </remarks>
public class LockReason : AuditableEntity
{
    private readonly List<LockReasonLink> _chain = [];

    /// <summary>Unique entity identifier.</summary>
    public Guid Id { get; protected set; }

    /// <summary>
    /// Canonical type name of the entity whose lock this chain explains.
    /// </summary>
    public string LockedEntityType { get; protected set; } = string.Empty;

    /// <summary>
    /// Canonical string-form identifier of the entity whose lock this
    /// chain explains.
    /// </summary>
    public string LockedEntityId { get; protected set; } = string.Empty;

    /// <summary>
    /// The ordered chain of links from the locked entity (first link) to
    /// the terminal root cause (last link).
    /// </summary>
    public IReadOnlyList<LockReasonLink> Chain => _chain;

    /// <summary>
    /// Parameterless constructor for the persistence layer. Do not call
    /// from application code.
    /// </summary>
    protected LockReason()
    {
    }

    /// <summary>
    /// Constructs a validated lock-reason chain.
    /// </summary>
    /// <param name="id">Unique entity identifier. Must not be
    /// <see cref="Guid.Empty"/>.</param>
    /// <param name="lockedEntityType">Canonical type name of the entity
    /// whose lock this chain explains. Must not be
    /// <see langword="null"/>, empty, or whitespace.</param>
    /// <param name="lockedEntityId">Canonical string-form identifier of
    /// the locked entity. Must not be <see langword="null"/>, empty, or
    /// whitespace.</param>
    /// <param name="chain">Ordered chain of links. Must contain at least
    /// one link, exactly one terminal link in the last position, and
    /// every non-terminal link must have a non-null
    /// <see cref="LockReasonLink.Because"/>.</param>
    /// <exception cref="ArgumentException">Thrown when any chain-shape
    /// constraint is violated, or when any string argument is null,
    /// empty, or whitespace, or when <paramref name="id"/> is
    /// <see cref="Guid.Empty"/>.</exception>
    /// <exception cref="ArgumentNullException">Thrown when
    /// <paramref name="chain"/> is <see langword="null"/>.</exception>
    public LockReason(
        Guid id,
        string lockedEntityType,
        string lockedEntityId,
        IEnumerable<LockReasonLink> chain)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Id must not be Guid.Empty.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(lockedEntityType);
        ArgumentException.ThrowIfNullOrWhiteSpace(lockedEntityId);
        ArgumentNullException.ThrowIfNull(chain);

        var materialized = chain.ToList();
        ValidateChainShape(materialized);

        Id = id;
        LockedEntityType = lockedEntityType;
        LockedEntityId = lockedEntityId;
        _chain = materialized;
    }

    private static void ValidateChainShape(List<LockReasonLink> chain)
    {
        if (chain.Count == 0)
        {
            throw new ArgumentException(
                "Chain must contain at least one link.",
                nameof(chain));
        }

        var terminalCount = 0;
        for (var i = 0; i < chain.Count; i++)
        {
            var link = chain[i];
            if (link.IsTerminal)
            {
                terminalCount++;
                if (i != chain.Count - 1)
                {
                    throw new ArgumentException(
                        "The terminal link must be the last link in the chain.",
                        nameof(chain));
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(link.Because))
                {
                    throw new ArgumentException(
                        "Non-terminal links must have a non-null Because connector.",
                        nameof(chain));
                }
            }
        }

        if (terminalCount != 1)
        {
            throw new ArgumentException(
                "Chain must contain exactly one terminal link.",
                nameof(chain));
        }
    }
}
