using System.Windows;

namespace EasySynQ.UI.Documents.CreateDocument;

/// <summary>
/// Production <see cref="ICreateDocumentPrompter"/>. Shows
/// <see cref="CreateDocumentDialog"/> modally; returns the user's
/// captured input on OK, or <see langword="null"/> on Cancel.
/// </summary>
/// <remarks>
/// Mirrors the <see cref="EasySynQ.UI.Signing.SignatureRolePrompter"/>
/// pattern (C4 precedent): the prompter constructs a fresh Window per
/// call (WPF Windows are single-<c>ShowDialog</c>), attaches the
/// application's <see cref="Application.MainWindow"/> as owner when
/// available, and returns the result after the modal closes.
/// </remarks>
public sealed class CreateDocumentPrompter : ICreateDocumentPrompter
{
    private readonly IFilePicker _filePicker;

    /// <summary>Constructs the prompter over its file-picker dependency.</summary>
    public CreateDocumentPrompter(IFilePicker filePicker)
    {
        ArgumentNullException.ThrowIfNull(filePicker);
        _filePicker = filePicker;
    }

    /// <inheritdoc />
    public Task<CreateDocumentResult?> PromptAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var dialog = new CreateDocumentDialog(_filePicker);
        if (Application.Current?.MainWindow is { } owner && !ReferenceEquals(owner, dialog))
        {
            dialog.Owner = owner;
        }

        var ok = dialog.ShowDialog();
        if (ok != true)
        {
            return Task.FromResult<CreateDocumentResult?>(null);
        }

        var result = new CreateDocumentResult(
            Number: dialog.ViewModel.Number,
            Title: dialog.ViewModel.Title,
            SelectedPdfPath: dialog.ViewModel.SelectedPdfPath);
        return Task.FromResult<CreateDocumentResult?>(result);
    }
}
