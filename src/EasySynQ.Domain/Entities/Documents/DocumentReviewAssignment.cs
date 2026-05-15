using EasySynQ.Domain.Common;
using EasySynQ.Domain.Enums;

namespace EasySynQ.Domain.Entities.Documents;

/// <summary>
/// Per-reviewer, per-revision review state (ADR 0008, SPEC §5.1).
/// Captures the assigned-reviewer model: when a revision is submitted
/// for review, one assignment row is written per named reviewer;
/// each reviewer signs to advance their own row's
/// <see cref="Status"/> from <see cref="DocumentReviewAssignmentStatus.Pending"/>
/// to <see cref="DocumentReviewAssignmentStatus.Signed"/>. When all
/// rows for the revision are Signed, the revision transitions to
/// <see cref="DocumentLifecycle.Approved"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Inheritance.</b> AuditableEntity. The signature row written when
/// a reviewer signs is a separate <c>Signature</c> entity; the
/// assignment row stores a soft reference to its id. The "signed
/// record" in SPEC §3.5's lockout sense is the Signature itself, not
/// the assignment row.
/// </para>
/// <para>
/// <b>Discarded preserves the audit trail.</b> When the revision
/// returns from InReview to Draft, in-progress assignments transition
/// to <see cref="DocumentReviewAssignmentStatus.Discarded"/>. Any
/// already-signed assignment keeps its <see cref="SignatureId"/>
/// reference — the Signature row is preserved for audit but no longer
/// counts toward approval.
/// </para>
/// </remarks>
public class DocumentReviewAssignment : AuditableEntity
{
    /// <summary>Unique entity identifier.</summary>
    public Guid Id { get; protected set; }

    /// <summary>
    /// Identifier of the DocumentRevision under review. Hard FK
    /// (within-aggregate) per ADR 0004.
    /// </summary>
    public Guid DocumentRevisionId { get; protected set; }

    /// <summary>
    /// Identifier of the assigned reviewer. Soft reference per ADR
    /// 0004 (no DB-level FK to Users).
    /// </summary>
    public Guid ReviewerUserId { get; protected set; }

    /// <summary>UTC instant at which the reviewer was assigned.</summary>
    public DateTime AssignedAtUtc { get; protected set; }

    /// <summary>
    /// Identifier of the user who assigned this reviewer (typically the
    /// author submitting for review, or a QM in the strict-gatekeeper
    /// policy). Soft reference per ADR 0004.
    /// </summary>
    public Guid AssignedByUserId { get; protected set; }

    /// <summary>Current assignment status.</summary>
    public DocumentReviewAssignmentStatus Status { get; protected set; }

    /// <summary>
    /// UTC instant at which the reviewer signed, or
    /// <see langword="null"/> if the reviewer has not yet signed.
    /// </summary>
    public DateTime? SignedAtUtc { get; protected set; }

    /// <summary>
    /// Identifier of the Signature row attesting to the reviewer's
    /// sign-off, or <see langword="null"/> if not yet signed. Soft
    /// reference per ADR 0004.
    /// </summary>
    public Guid? SignatureId { get; protected set; }

    /// <summary>
    /// Parameterless constructor for the persistence layer. Do not call
    /// from application code.
    /// </summary>
    protected DocumentReviewAssignment()
    {
    }

    /// <summary>
    /// Constructs a new reviewer assignment in
    /// <see cref="DocumentReviewAssignmentStatus.Pending"/> state.
    /// Signing transitions are the lifecycle service's responsibility
    /// (C3 scope).
    /// </summary>
    /// <param name="id">Unique entity identifier. Must not be
    /// <see cref="Guid.Empty"/>.</param>
    /// <param name="documentRevisionId">Revision under review. Must not
    /// be <see cref="Guid.Empty"/>.</param>
    /// <param name="reviewerUserId">Assigned reviewer's user id. Must
    /// not be <see cref="Guid.Empty"/>.</param>
    /// <param name="assignedAtUtc">UTC instant of assignment. Must be of
    /// <see cref="DateTimeKind.Utc"/>.</param>
    /// <param name="assignedByUserId">User who made the assignment. Must
    /// not be <see cref="Guid.Empty"/>.</param>
    /// <exception cref="ArgumentException">Thrown when any input fails
    /// validation.</exception>
    public DocumentReviewAssignment(
        Guid id,
        Guid documentRevisionId,
        Guid reviewerUserId,
        DateTime assignedAtUtc,
        Guid assignedByUserId)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Id must not be Guid.Empty.", nameof(id));
        }
        if (documentRevisionId == Guid.Empty)
        {
            throw new ArgumentException(
                "DocumentRevisionId must not be Guid.Empty.",
                nameof(documentRevisionId));
        }
        if (reviewerUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "ReviewerUserId must not be Guid.Empty.",
                nameof(reviewerUserId));
        }
        if (assignedByUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "AssignedByUserId must not be Guid.Empty.",
                nameof(assignedByUserId));
        }
        if (assignedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "AssignedAtUtc must have DateTimeKind.Utc.",
                nameof(assignedAtUtc));
        }

        Id = id;
        DocumentRevisionId = documentRevisionId;
        ReviewerUserId = reviewerUserId;
        AssignedAtUtc = assignedAtUtc;
        AssignedByUserId = assignedByUserId;
        Status = DocumentReviewAssignmentStatus.Pending;
    }
}
