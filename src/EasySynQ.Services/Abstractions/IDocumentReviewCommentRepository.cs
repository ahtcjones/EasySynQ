using EasySynQ.Domain.Entities.Documents;

namespace EasySynQ.Services.Abstractions;

/// <summary>
/// Repository surface for <see cref="DocumentReviewComment"/> rows
/// (ADR 0008 C6b). Adds the per-revision fetch used by the
/// detail-view comment panel to render the chronological comment
/// thread when a revision is in
/// <see cref="EasySynQ.Domain.Enums.DocumentLifecycle.InReview"/>.
/// </summary>
public interface IDocumentReviewCommentRepository
    : IRepository<DocumentReviewComment, Guid>
{
    /// <summary>
    /// Returns every <see cref="DocumentReviewComment"/> attached to
    /// the supplied revision. Soft-deleted rows are excluded by the
    /// standard soft-delete filter. Ordering is the caller's
    /// responsibility (the comment-panel VM sorts the returned list);
    /// the repository remains order-neutral so consumers with
    /// different ordering needs (chronological forward for a
    /// detail-panel display; reverse-chronological for a "latest
    /// activity" tile) do not contend over a baked-in sort.
    /// </summary>
    /// <param name="documentRevisionId">Revision whose comments to
    /// fetch.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matching comments, possibly empty.</returns>
    Task<IReadOnlyList<DocumentReviewComment>> GetByRevisionIdAsync(
        Guid documentRevisionId,
        CancellationToken cancellationToken);
}
