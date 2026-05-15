using EasySynQ.Domain.Entities.Documents;

namespace EasySynQ.Services.Abstractions;

/// <summary>
/// Repository surface for <see cref="DocumentReviewAssignment"/> rows
/// (ADR 0008 C3). Adds the per-revision fetch used by the lifecycle
/// service to determine reviewer-completion state.
/// </summary>
public interface IDocumentReviewAssignmentRepository
    : IRepository<DocumentReviewAssignment, Guid>
{
    /// <summary>
    /// Returns every <see cref="DocumentReviewAssignment"/> attached to
    /// the supplied revision, including any in
    /// <see cref="EasySynQ.Domain.Enums.DocumentReviewAssignmentStatus.Discarded"/>
    /// state (callers filter on <c>Status</c> as appropriate). Soft-
    /// deleted rows are excluded by the standard soft-delete filter.
    /// </summary>
    /// <param name="documentRevisionId">Revision whose assignments to
    /// fetch.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matching assignments, possibly empty.</returns>
    Task<IReadOnlyList<DocumentReviewAssignment>> GetByRevisionIdAsync(
        Guid documentRevisionId,
        CancellationToken cancellationToken);
}
