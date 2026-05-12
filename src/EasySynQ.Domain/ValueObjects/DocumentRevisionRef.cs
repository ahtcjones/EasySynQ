namespace EasySynQ.Domain.ValueObjects;

/// <summary>
/// Reference to a specific revision of a controlled document, captured at the
/// moment the reference was created. The reference snapshots the document
/// identifier, the revision tag, and the revision's effective-from timestamp
/// so that the reference remains stable across later edits to the document's
/// metadata.
/// </summary>
/// <remarks>
/// Used by entities that link to a specific document revision (for example,
/// Part Master's <c>RequiredSOPs</c> collection per SPEC §5.3). The captured
/// fields are a snapshot, not a live join — even if the document's revision
/// tag is renamed or its effective-from is corrected, the reference
/// continues to describe the linked revision as it was at link time.
/// </remarks>
public sealed record DocumentRevisionRef
{
    /// <summary>Identifier of the controlled document.</summary>
    public Guid DocumentId { get; init; }

    /// <summary>
    /// The revision label as displayed (for example, <c>"Rev B"</c>).
    /// </summary>
    public string RevisionTag { get; init; }

    /// <summary>
    /// The UTC instant the linked revision became effective, captured at the
    /// time the link was created.
    /// </summary>
    public DateTime EffectiveFromUtc { get; init; }

    /// <summary>
    /// Constructs a validated document-revision reference.
    /// </summary>
    /// <param name="documentId">Identifier of the controlled document. Must
    /// not be <see cref="Guid.Empty"/>.</param>
    /// <param name="revisionTag">Display label of the linked revision. Must
    /// not be <see langword="null"/>, empty, or whitespace.</param>
    /// <param name="effectiveFromUtc">UTC instant the linked revision became
    /// effective. Must be of <see cref="DateTimeKind.Utc"/>.</param>
    /// <exception cref="ArgumentException">Thrown when any input fails
    /// validation.</exception>
    public DocumentRevisionRef(Guid documentId, string revisionTag, DateTime effectiveFromUtc)
    {
        if (documentId == Guid.Empty)
        {
            throw new ArgumentException(
                "DocumentId must not be Guid.Empty.",
                nameof(documentId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(revisionTag);

        if (effectiveFromUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "EffectiveFromUtc must have DateTimeKind.Utc.",
                nameof(effectiveFromUtc));
        }

        DocumentId = documentId;
        RevisionTag = revisionTag;
        EffectiveFromUtc = effectiveFromUtc;
    }
}
