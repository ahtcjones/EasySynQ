using EasySynQ.Domain.Common;
using EasySynQ.Domain.Enums;

namespace EasySynQ.Domain.Entities.Documents;

/// <summary>
/// A single revision of an internal <see cref="Document"/> (SPEC §5.1,
/// ADR 0008). Carries the author signature and any reviewer signatures;
/// the revision is the primary signed entity in the Document aggregate.
/// </summary>
/// <remarks>
/// <para>
/// <b>Inheritance.</b> DocumentRevision derives from
/// <see cref="SignableEntity"/> — it carries signatures directly
/// (<see cref="AuthorSignatureId"/>) and via the reviewer-assignment
/// table (one Signature per signed assignment). The
/// <see cref="SignableEntity.LockedAtUtc"/> field captures the moment
/// the revision first transitioned out of <see cref="DocumentLifecycle.Draft"/>
/// state, which is the SPEC §3.5 immutability boundary.
/// </para>
/// <para>
/// <b>Active is derived, not stored.</b> The <see cref="Lifecycle"/>
/// enum tracks the explicit lifecycle state (Draft, InReview, Approved,
/// Superseded, Archived). The "Active" sub-state of Approved is
/// computed at read time from <see cref="ApprovedAtUtc"/> +
/// <see cref="EffectiveFromUtc"/> + presence of a later superseding
/// revision; see SPEC §5.1.
/// </para>
/// <para>
/// <b>Author signing happens at submission, not creation.</b> A newly-
/// constructed revision is in Draft with no signature; the author
/// signs when the submission-for-review transaction transitions
/// Draft → InReview (C3 scope). <see cref="AuthorSignatureId"/> is
/// nullable; null while in Draft, populated from the moment of
/// submission onward.
/// </para>
/// </remarks>
public class DocumentRevision : SignableEntity
{
    /// <summary>Unique entity identifier.</summary>
    public Guid Id { get; protected set; }

    /// <summary>
    /// Identifier of the parent <see cref="Document"/>. Hard FK
    /// (within-aggregate) per ADR 0004.
    /// </summary>
    public Guid DocumentId { get; protected set; }

    /// <summary>
    /// Org-defined revision label (e.g., "Rev A", "Rev 2026-01"). No
    /// uniqueness constraint across documents; labels are unique within
    /// a single Document's revision history but the enforcement of that
    /// is a service-layer responsibility.
    /// </summary>
    public string RevisionLabel { get; protected set; } = string.Empty;

    /// <summary>
    /// Explicit lifecycle state. Stored as TEXT in the DB; see
    /// <see cref="DocumentLifecycle"/> for the state-machine semantics.
    /// </summary>
    public DocumentLifecycle Lifecycle { get; protected set; }

    /// <summary>
    /// UTC instant from which this revision becomes Active once approved,
    /// or <see langword="null"/> if it becomes Active immediately on
    /// approval. May be in the future to support pre-scheduled
    /// effectiveness (SPEC §3.7).
    /// </summary>
    public DateTime? EffectiveFromUtc { get; protected set; }

    /// <summary>
    /// UTC instant at which the revision was approved, or
    /// <see langword="null"/> while in Draft or InReview. Populated
    /// atomically with the lifecycle transition to
    /// <see cref="DocumentLifecycle.Approved"/>.
    /// </summary>
    public DateTime? ApprovedAtUtc { get; protected set; }

    /// <summary>
    /// Identifier of the <c>VaultBlob</c> holding this revision's
    /// content, or <see langword="null"/> if no file is attached yet.
    /// Hard FK (within-aggregate) per ADR 0004.
    /// </summary>
    public Guid? VaultBlobId { get; protected set; }

    /// <summary>
    /// Identifier of the user who authored this revision. Soft reference
    /// per ADR 0004 (no DB-level FK to Users).
    /// </summary>
    public Guid AuthorUserId { get; protected set; }

    /// <summary>
    /// Identifier of the author's submission signature, or
    /// <see langword="null"/> while the revision is in Draft. Populated
    /// atomically with the lifecycle transition to InReview. Soft
    /// reference per ADR 0004 (no DB-level FK to Signatures).
    /// </summary>
    public Guid? AuthorSignatureId { get; protected set; }

    /// <summary>
    /// Parameterless constructor for the persistence layer. Do not call
    /// from application code.
    /// </summary>
    protected DocumentRevision()
    {
    }

    /// <summary>
    /// Constructs a new DocumentRevision in <see cref="DocumentLifecycle.Draft"/>.
    /// Lifecycle transitions, signatures, and approval timestamps are
    /// the lifecycle service's responsibility (C3 scope).
    /// </summary>
    /// <param name="id">Unique entity identifier. Must not be
    /// <see cref="Guid.Empty"/>.</param>
    /// <param name="documentId">Parent Document identifier. Must not be
    /// <see cref="Guid.Empty"/>.</param>
    /// <param name="revisionLabel">Org-defined revision label. Must not
    /// be <see langword="null"/>, empty, or whitespace.</param>
    /// <param name="authorUserId">Author's user identifier. Must not be
    /// <see cref="Guid.Empty"/>.</param>
    /// <exception cref="ArgumentException">Thrown when any input fails
    /// validation.</exception>
    public DocumentRevision(Guid id, Guid documentId, string revisionLabel, Guid authorUserId)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Id must not be Guid.Empty.", nameof(id));
        }
        if (documentId == Guid.Empty)
        {
            throw new ArgumentException("DocumentId must not be Guid.Empty.", nameof(documentId));
        }
        if (authorUserId == Guid.Empty)
        {
            throw new ArgumentException("AuthorUserId must not be Guid.Empty.", nameof(authorUserId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(revisionLabel);

        Id = id;
        DocumentId = documentId;
        RevisionLabel = revisionLabel;
        AuthorUserId = authorUserId;
        Lifecycle = DocumentLifecycle.Draft;
    }
}
