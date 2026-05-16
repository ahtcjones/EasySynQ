namespace EasySynQ.UI.Documents.EditMetadata;

/// <summary>
/// Abstraction over the modal "edit metadata" dialog so the
/// <c>DocumentDetailViewModel</c>'s Edit Metadata command can be
/// unit-tested without instantiating a WPF Window. Mirrors the
/// <see cref="EasySynQ.UI.Documents.CreateDocument.ICreateDocumentPrompter"/>
/// shape (small dialog, single result, null-on-cancel).
/// </summary>
public interface IEditMetadataPrompter
{
    /// <summary>
    /// Shows the edit-metadata dialog modally pre-populated with the
    /// supplied current values, and returns the user's updated values
    /// or <see langword="null"/> when the user cancels.
    /// </summary>
    /// <param name="currentNumber">Current document number, used to
    /// pre-populate the dialog. Must not be null/empty.</param>
    /// <param name="currentTitle">Current document title, used to
    /// pre-populate the dialog. Must not be null/empty.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The captured <see cref="EditMetadataResult"/> on OK,
    /// or <see langword="null"/> on Cancel.</returns>
    Task<EditMetadataResult?> PromptAsync(
        string currentNumber,
        string currentTitle,
        CancellationToken cancellationToken);
}
