namespace EasySynQ.UI.Documents.ReviewAndSign;

/// <summary>
/// Abstraction over the modal "review and sign" dialog so the
/// <c>DocumentDetailViewModel</c>'s ReviewAndSign command can be
/// unit-tested without instantiating a WPF Window (ADR 0008 C6b).
/// Mirrors the
/// <see cref="EasySynQ.UI.Documents.SubmitForReview.ISubmitForReviewPrompter"/>
/// shape established at stop 3.
/// </summary>
public interface IReviewAndSignPrompter
{
    /// <summary>
    /// Shows the review-and-sign dialog modally and returns
    /// whether the sign was committed. Like
    /// <see cref="EasySynQ.UI.Documents.SubmitForReview.ISubmitForReviewPrompter"/>,
    /// when this returns <see langword="true"/> the underlying
    /// sign transaction has already committed and the caller's
    /// responsibility is purely UI refresh.
    /// </summary>
    /// <param name="revisionId">Id of the InReview revision being
    /// signed. Must not be <see cref="Guid.Empty"/>.</param>
    /// <param name="documentTitle">Title of the parent Document
    /// (rendered in the confirmation message). Must not be
    /// null/empty/whitespace.</param>
    /// <param name="revisionLabel">Revision label (rendered in
    /// the confirmation message). Must not be
    /// null/empty/whitespace.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when the sign committed;
    /// <see langword="false"/> when the user cancelled (role-picker
    /// cancel or explicit Cancel button).</returns>
    Task<bool> PromptAsync(
        Guid revisionId,
        string documentTitle,
        string revisionLabel,
        CancellationToken cancellationToken);
}
