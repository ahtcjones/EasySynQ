using EasySynQ.Domain.Common;

namespace EasySynQ.Domain.Entities.Documents;

/// <summary>
/// A reviewer's comment on a <see cref="DocumentRevision"/> in the
/// InReview state (ADR 0008). The minimal-viable in-review commenting
/// surface for Phase 2 — reviewers post comments, the author sees
/// them when the document returns to Draft. Richer reviewer interaction
/// (PDF markup, inline annotations, redline diffs) is deferred to a
/// later phase.
/// </summary>
/// <remarks>
/// <para>
/// <b>Inheritance.</b> AuditableEntity — comments are audit-logged but
/// not signed. The ADR is explicit: "auditable but not signed."
/// </para>
/// <para>
/// <b>BodyText has no DB-level length cap.</b> Stored as TEXT. The UI
/// may enforce a soft cap for sensible display, but the data layer
/// imposes no limit.
/// </para>
/// </remarks>
public class DocumentReviewComment : AuditableEntity
{
    /// <summary>Unique entity identifier.</summary>
    public Guid Id { get; protected set; }

    /// <summary>
    /// Identifier of the DocumentRevision being commented on. Hard FK
    /// (within-aggregate) per ADR 0004.
    /// </summary>
    public Guid DocumentRevisionId { get; protected set; }

    /// <summary>
    /// Identifier of the comment's author. Soft reference per ADR
    /// 0004 (no DB-level FK to Users).
    /// </summary>
    public Guid AuthorUserId { get; protected set; }

    /// <summary>Free-form comment text. No length cap at the DB level.</summary>
    public string BodyText { get; protected set; } = string.Empty;

    /// <summary>UTC instant at which the comment was posted.</summary>
    public DateTime CreatedAtUtc { get; protected set; }

    /// <summary>
    /// Parameterless constructor for the persistence layer. Do not call
    /// from application code.
    /// </summary>
    protected DocumentReviewComment()
    {
    }

    /// <summary>
    /// Constructs a new review comment.
    /// </summary>
    /// <param name="id">Unique entity identifier. Must not be
    /// <see cref="Guid.Empty"/>.</param>
    /// <param name="documentRevisionId">Revision being commented on. Must
    /// not be <see cref="Guid.Empty"/>.</param>
    /// <param name="authorUserId">Author's user id. Must not be
    /// <see cref="Guid.Empty"/>.</param>
    /// <param name="bodyText">Comment body. Must not be
    /// <see langword="null"/>, empty, or whitespace.</param>
    /// <param name="createdAtUtc">UTC instant of posting. Must be of
    /// <see cref="DateTimeKind.Utc"/>.</param>
    /// <exception cref="ArgumentException">Thrown when any input fails
    /// validation.</exception>
    public DocumentReviewComment(
        Guid id,
        Guid documentRevisionId,
        Guid authorUserId,
        string bodyText,
        DateTime createdAtUtc)
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
        if (authorUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "AuthorUserId must not be Guid.Empty.",
                nameof(authorUserId));
        }
        if (createdAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "CreatedAtUtc must have DateTimeKind.Utc.",
                nameof(createdAtUtc));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(bodyText);

        Id = id;
        DocumentRevisionId = documentRevisionId;
        AuthorUserId = authorUserId;
        BodyText = bodyText;
        CreatedAtUtc = createdAtUtc;
    }
}
