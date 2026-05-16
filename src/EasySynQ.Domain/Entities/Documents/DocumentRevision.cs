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
    /// Reason supplied by the user who most recently returned this
    /// revision from <see cref="DocumentLifecycle.InReview"/> to
    /// <see cref="DocumentLifecycle.Draft"/>, or
    /// <see langword="null"/> if the revision has never been returned
    /// (or has since been re-submitted, which clears the field).
    /// Persisted as plain text on the revision row so the author
    /// sees the reviewer's reason when the revision lands back in
    /// Draft. Captured in the audit log's revision-Update row's
    /// <c>After</c> snapshot naturally; preserved historically there
    /// even after re-submission clears the live column.
    /// </summary>
    public string? LastReturnToDraftReason { get; protected set; }

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

    /// <summary>
    /// Sets <see cref="EffectiveFromUtc"/>. Allowed only while the
    /// revision is in <see cref="DocumentLifecycle.Draft"/>; once a
    /// revision is submitted, its effective date is bound to the author
    /// signature's payload and may not be silently changed. Pass
    /// <see langword="null"/> to clear (revision will become Active
    /// immediately on approval).
    /// </summary>
    /// <param name="effectiveFromUtc">UTC instant from which the
    /// revision becomes Active once approved, or <see langword="null"/>
    /// for immediate. Must be of <see cref="DateTimeKind.Utc"/> when
    /// non-null. May be in the future per SPEC §3.7.</param>
    /// <exception cref="InvalidOperationException">Thrown when the
    /// revision is not in <see cref="DocumentLifecycle.Draft"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when
    /// <paramref name="effectiveFromUtc"/> is non-null and not of
    /// <see cref="DateTimeKind.Utc"/>.</exception>
    public void SetEffectiveFromUtc(DateTime? effectiveFromUtc)
    {
        if (Lifecycle != DocumentLifecycle.Draft)
        {
            throw new InvalidOperationException(
                $"Cannot set EffectiveFromUtc on a revision in '{Lifecycle}' state; only Draft revisions are editable.");
        }
        if (effectiveFromUtc is { } value && value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "EffectiveFromUtc must have DateTimeKind.Utc.",
                nameof(effectiveFromUtc));
        }

        EffectiveFromUtc = effectiveFromUtc;
    }

    /// <summary>
    /// Transitions the revision from <see cref="DocumentLifecycle.Draft"/>
    /// to <see cref="DocumentLifecycle.InReview"/>. Stamps the author's
    /// submission signature reference and locks the entity (sets
    /// <see cref="EasySynQ.Domain.Common.SignableEntity.LockedAtUtc"/>).
    /// The lock is one-way per the SignableEntity contract — even if the
    /// revision later returns to Draft, <c>LockedAtUtc</c> remains set.
    /// </summary>
    /// <param name="authorSignatureId">Id of the author's submission
    /// <c>Signature</c>. Must not be <see cref="Guid.Empty"/>.</param>
    /// <param name="lockedAtUtc">UTC instant at which the lock is
    /// recorded. Must be of <see cref="DateTimeKind.Utc"/>.</param>
    /// <exception cref="InvalidOperationException">Thrown when the
    /// revision is not in Draft.</exception>
    /// <exception cref="ArgumentException">Thrown when any input fails
    /// validation.</exception>
    public void Submit(Guid authorSignatureId, DateTime lockedAtUtc)
    {
        if (Lifecycle != DocumentLifecycle.Draft)
        {
            throw new InvalidOperationException(
                $"Cannot submit revision {Id} for review: current state is '{Lifecycle}', expected '{nameof(DocumentLifecycle.Draft)}'.");
        }
        if (authorSignatureId == Guid.Empty)
        {
            throw new ArgumentException(
                "AuthorSignatureId must not be Guid.Empty.",
                nameof(authorSignatureId));
        }
        if (lockedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "LockedAtUtc must have DateTimeKind.Utc.",
                nameof(lockedAtUtc));
        }

        Lifecycle = DocumentLifecycle.InReview;
        AuthorSignatureId = authorSignatureId;
        // Re-submitting a revision that was previously returned to
        // Draft clears the live LastReturnToDraftReason — the live
        // column tracks the *current* Draft-state reason only. The
        // prior return reason is preserved in the audit log's
        // revision-Update row's Before/After snapshots.
        LastReturnToDraftReason = null;
        // LockedAtUtc has a protected setter on SignableEntity (visible
        // here because we derive from it). One-way per the
        // SignableEntity contract — once set, never reset to null even
        // if the revision later returns to Draft.
        if (LockedAtUtc is null)
        {
            LockedAtUtc = lockedAtUtc;
        }
    }

    /// <summary>
    /// Transitions the revision from <see cref="DocumentLifecycle.InReview"/>
    /// back to <see cref="DocumentLifecycle.Draft"/>. Clears
    /// <see cref="AuthorSignatureId"/> per ADR 0008 C3 plan §G Q3 — the
    /// previous author signature is preserved in the audit log; on
    /// re-submission a fresh author signature attests to the
    /// (potentially edited) revision state. Stamps
    /// <see cref="LastReturnToDraftReason"/> with the supplied reason
    /// so the author sees why the revision was returned.
    /// <c>LockedAtUtc</c> is NOT cleared (one-way per
    /// <c>SignableEntity</c>'s contract).
    /// </summary>
    /// <param name="reason">Free-form reason text supplied by the
    /// caller (reviewer or author with <c>Document.ReturnForEdits</c>).
    /// Must not be <see langword="null"/>, empty, or whitespace per
    /// the C6b plan §E "required-reason text box" decision.</param>
    /// <exception cref="InvalidOperationException">Thrown when the
    /// revision is not in InReview.</exception>
    /// <exception cref="ArgumentException">Thrown when
    /// <paramref name="reason"/> is null, empty, or whitespace.</exception>
    public void ReturnToDraft(string reason)
    {
        if (Lifecycle != DocumentLifecycle.InReview)
        {
            throw new InvalidOperationException(
                $"Cannot return revision {Id} to Draft: current state is '{Lifecycle}', expected '{nameof(DocumentLifecycle.InReview)}'.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        Lifecycle = DocumentLifecycle.Draft;
        AuthorSignatureId = null;
        LastReturnToDraftReason = reason;
    }

    /// <summary>
    /// Transitions the revision from <see cref="DocumentLifecycle.InReview"/>
    /// to <see cref="DocumentLifecycle.Approved"/>. Stamps
    /// <see cref="ApprovedAtUtc"/>. The "Active" sub-state is derived at
    /// query time from <see cref="ApprovedAtUtc"/> +
    /// <see cref="EffectiveFromUtc"/>; this method does not stamp it.
    /// </summary>
    /// <param name="approvedAtUtc">UTC instant of approval — typically
    /// the same instant as the final reviewer's signature timestamp.
    /// Must be of <see cref="DateTimeKind.Utc"/>.</param>
    /// <exception cref="InvalidOperationException">Thrown when the
    /// revision is not in InReview.</exception>
    /// <exception cref="ArgumentException">Thrown when
    /// <paramref name="approvedAtUtc"/> is not UTC.</exception>
    public void Approve(DateTime approvedAtUtc)
    {
        if (Lifecycle != DocumentLifecycle.InReview)
        {
            throw new InvalidOperationException(
                $"Cannot approve revision {Id}: current state is '{Lifecycle}', expected '{nameof(DocumentLifecycle.InReview)}'.");
        }
        if (approvedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "ApprovedAtUtc must have DateTimeKind.Utc.",
                nameof(approvedAtUtc));
        }

        Lifecycle = DocumentLifecycle.Approved;
        ApprovedAtUtc = approvedAtUtc;
    }

    /// <summary>
    /// Transitions the revision from <see cref="DocumentLifecycle.Approved"/>
    /// to <see cref="DocumentLifecycle.Superseded"/>. Per ADR 0008 C3
    /// plan §G Q9, this fires immediately when a successor revision
    /// becomes Approved, regardless of the successor's
    /// <see cref="EffectiveFromUtc"/>. The Approved-to-Effective gap is
    /// surfaced by the as-of resolver returning no Active revision for
    /// the document during the gap.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the
    /// revision is not in Approved.</exception>
    public void Supersede()
    {
        if (Lifecycle != DocumentLifecycle.Approved)
        {
            throw new InvalidOperationException(
                $"Cannot supersede revision {Id}: current state is '{Lifecycle}', expected '{nameof(DocumentLifecycle.Approved)}'.");
        }

        Lifecycle = DocumentLifecycle.Superseded;
    }

    /// <summary>
    /// Transitions the revision from <see cref="DocumentLifecycle.Approved"/>
    /// to <see cref="DocumentLifecycle.Archived"/>. Used when the parent
    /// <c>Document</c> is retired — the current Active revision moves to
    /// Archived and no successor is created.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the
    /// revision is not in Approved.</exception>
    public void Archive()
    {
        if (Lifecycle != DocumentLifecycle.Approved)
        {
            throw new InvalidOperationException(
                $"Cannot archive revision {Id}: current state is '{Lifecycle}', expected '{nameof(DocumentLifecycle.Approved)}'.");
        }

        Lifecycle = DocumentLifecycle.Archived;
    }

    /// <summary>
    /// Attaches (or replaces) the <see cref="VaultBlobId"/> reference
    /// (ADR 0008 C6a). Allowed only while the revision is in
    /// <see cref="DocumentLifecycle.Draft"/>; once submitted, the
    /// attached content is bound to the author's signature payload and
    /// may not be changed. Replacement overwrites the prior id; the
    /// orphaned vault blob remains in the vault per the C6a
    /// "deferred indefinitely" cleanup decision.
    /// </summary>
    /// <param name="vaultBlobId">Identifier of the
    /// <c>VaultBlob</c> backing this revision's content. Must not be
    /// <see cref="Guid.Empty"/>.</param>
    /// <exception cref="InvalidOperationException">Thrown when the
    /// revision is not in <see cref="DocumentLifecycle.Draft"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when
    /// <paramref name="vaultBlobId"/> is <see cref="Guid.Empty"/>.</exception>
    public void AttachVaultBlob(Guid vaultBlobId)
    {
        if (Lifecycle != DocumentLifecycle.Draft)
        {
            throw new InvalidOperationException(
                $"Cannot attach a vault blob to revision {Id}: current state is '{Lifecycle}', expected '{nameof(DocumentLifecycle.Draft)}'.");
        }
        if (vaultBlobId == Guid.Empty)
        {
            throw new ArgumentException(
                "VaultBlobId must not be Guid.Empty.",
                nameof(vaultBlobId));
        }

        VaultBlobId = vaultBlobId;
    }
}
