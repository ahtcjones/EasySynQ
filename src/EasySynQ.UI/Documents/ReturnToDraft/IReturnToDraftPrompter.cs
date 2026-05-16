namespace EasySynQ.UI.Documents.ReturnToDraft;

/// <summary>
/// Abstraction over the modal "return to draft" dialog so the
/// <c>DocumentDetailViewModel</c>'s ReturnToDraft command can be
/// unit-tested without instantiating a WPF Window (ADR 0008 C6b).
/// Mirrors the
/// <see cref="EasySynQ.UI.Documents.SubmitForReview.ISubmitForReviewPrompter"/>
/// /
/// <see cref="EasySynQ.UI.Documents.ReviewAndSign.IReviewAndSignPrompter"/>
/// shape.
/// </summary>
public interface IReturnToDraftPrompter
{
    /// <summary>
    /// Shows the return-to-draft dialog modally and returns
    /// whether the transition committed. When this returns
    /// <see langword="true"/> the underlying lifecycle transaction
    /// has already committed and the caller's responsibility is
    /// purely UI refresh.
    /// </summary>
    /// <param name="revisionId">Id of the InReview revision being
    /// returned. Must not be <see cref="Guid.Empty"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when the return committed;
    /// <see langword="false"/> when the user cancelled.</returns>
    Task<bool> PromptAsync(Guid revisionId, CancellationToken cancellationToken);
}
