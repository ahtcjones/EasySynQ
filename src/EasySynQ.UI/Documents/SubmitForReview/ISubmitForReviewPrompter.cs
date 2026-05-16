namespace EasySynQ.UI.Documents.SubmitForReview;

/// <summary>
/// Abstraction over the modal "submit for review" dialog so the
/// <c>DocumentDetailViewModel</c>'s Submit command can be unit-tested
/// without instantiating a WPF Window (ADR 0008 C6b). Mirrors the
/// <see cref="EasySynQ.UI.Documents.EditMetadata.IEditMetadataPrompter"/>
/// precedent.
/// </summary>
public interface ISubmitForReviewPrompter
{
    /// <summary>
    /// Shows the submit-for-review dialog modally and returns
    /// whether the submission was completed. Unlike the C6a dumb-
    /// VM prompters, this prompter's dialog VM owns the
    /// service-call side effects — when the returned value is
    /// <see langword="true"/> the submit transaction has already
    /// committed and the caller's responsibility is purely UI
    /// refresh.
    /// </summary>
    /// <param name="revisionId">Id of the Draft revision being
    /// submitted. Must not be <see cref="Guid.Empty"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when the submission
    /// committed; <see langword="false"/> when the user cancelled
    /// the dialog (either via the explicit Cancel button or by
    /// closing the window).</returns>
    Task<bool> PromptAsync(Guid revisionId, CancellationToken cancellationToken);
}
