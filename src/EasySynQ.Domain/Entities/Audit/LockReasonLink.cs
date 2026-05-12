namespace EasySynQ.Domain.Entities.Audit;

/// <summary>
/// One link in a lock-reason causal chain (SPEC §4.3). The chain is
/// constructed as part of a <see cref="LockReason"/>; non-terminal links
/// connect to the next link via a "because …" connector, and the final
/// link is the root cause (terminal).
/// </summary>
/// <remarks>
/// This is a value object — equality is structural and links are
/// immutable once constructed. Chain-level validation
/// (at-least-one-link, exactly-one-terminal, terminal-last,
/// non-terminal-must-have-because) is enforced by
/// <see cref="LockReason"/>'s constructor, not by individual links.
/// </remarks>
public sealed record LockReasonLink
{
    /// <summary>
    /// Category label for the link (for example, <c>"Job"</c>,
    /// <c>"NCR"</c>, <c>"Part Master"</c>). Rendered as a chip in the
    /// lock-inspector UI.
    /// </summary>
    public string Tag { get; init; }

    /// <summary>
    /// Display identifier for this link (for example,
    /// <c>"J-2026-0847"</c>, <c>"NCR-2026-0033"</c>,
    /// <c>"AD-1184-C Rev 3"</c>).
    /// </summary>
    public string Id { get; init; }

    /// <summary>Free-text detail explaining the state of this link.</summary>
    public string Detail { get; init; }

    /// <summary>
    /// Optional navigation target type for the lock-inspector's
    /// click-through. <see langword="null"/> for links that do not
    /// navigate (typically terminal links and purely informational rows).
    /// </summary>
    public string? NavigationEntityType { get; init; }

    /// <summary>
    /// Optional navigation target identifier paired with
    /// <see cref="NavigationEntityType"/>.
    /// </summary>
    public string? NavigationEntityId { get; init; }

    /// <summary>
    /// "because …" connector text rendered between this link and the
    /// next. Must be non-null for non-terminal links (enforced by
    /// <see cref="LockReason"/>'s chain validation); should be
    /// <see langword="null"/> for terminal links.
    /// </summary>
    public string? Because { get; init; }

    /// <summary>
    /// True when this link is the root cause and the chain ends here.
    /// Exactly one link in a chain has this flag set, and it must be the
    /// last link (enforced by <see cref="LockReason"/>).
    /// </summary>
    public bool IsTerminal { get; init; }

    /// <summary>
    /// Constructs a validated link.
    /// </summary>
    /// <param name="tag">Category label. Must not be
    /// <see langword="null"/>, empty, or whitespace.</param>
    /// <param name="id">Display identifier. Must not be
    /// <see langword="null"/>, empty, or whitespace.</param>
    /// <param name="detail">Free-text detail. Must not be
    /// <see langword="null"/>, empty, or whitespace.</param>
    /// <param name="navigationEntityType">Optional navigation type.</param>
    /// <param name="navigationEntityId">Optional navigation identifier.</param>
    /// <param name="because">Optional connector text.</param>
    /// <param name="isTerminal">Whether this link is the root cause.</param>
    /// <exception cref="ArgumentException">Thrown when any required
    /// field is null, empty, or whitespace.</exception>
    public LockReasonLink(
        string tag,
        string id,
        string detail,
        string? navigationEntityType,
        string? navigationEntityId,
        string? because,
        bool isTerminal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);

        Tag = tag;
        Id = id;
        Detail = detail;
        NavigationEntityType = navigationEntityType;
        NavigationEntityId = navigationEntityId;
        Because = because;
        IsTerminal = isTerminal;
    }
}
