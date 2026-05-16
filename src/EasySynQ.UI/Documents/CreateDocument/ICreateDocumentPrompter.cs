namespace EasySynQ.UI.Documents.CreateDocument;

/// <summary>
/// Abstraction over the modal "create document" dialog so the
/// <c>DocumentListViewModel</c>'s Create command can be unit-tested
/// without instantiating a WPF Window. Mirrors the
/// <see cref="EasySynQ.UI.Signing.ISignatureRolePrompter"/> precedent
/// established in C4.
/// </summary>
public interface ICreateDocumentPrompter
{
    /// <summary>
    /// Shows the create-document dialog modally and returns the
    /// user's confirmed input, or <see langword="null"/> when the
    /// user cancels.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The captured <see cref="CreateDocumentResult"/> on OK,
    /// or <see langword="null"/> on Cancel.</returns>
    Task<CreateDocumentResult?> PromptAsync(CancellationToken cancellationToken);
}
