using System.Windows;

namespace EasySynQ.UI.Documents.EditMetadata;

/// <summary>
/// Production <see cref="IEditMetadataPrompter"/>. Shows
/// <see cref="EditMetadataDialog"/> modally; returns the user's
/// updated values on OK, or <see langword="null"/> on Cancel.
/// Mirrors the <see cref="CreateDocumentPrompter"/> shape.
/// </summary>
public sealed class EditMetadataPrompter : IEditMetadataPrompter
{
    /// <inheritdoc />
    public Task<EditMetadataResult?> PromptAsync(
        string currentNumber,
        string currentTitle,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var dialog = new EditMetadataDialog(currentNumber, currentTitle);
        if (Application.Current?.MainWindow is { } owner && !ReferenceEquals(owner, dialog))
        {
            dialog.Owner = owner;
        }

        var ok = dialog.ShowDialog();
        if (ok != true)
        {
            return Task.FromResult<EditMetadataResult?>(null);
        }

        var result = new EditMetadataResult(
            NewNumber: dialog.ViewModel.Number,
            NewTitle: dialog.ViewModel.Title);
        return Task.FromResult<EditMetadataResult?>(result);
    }
}
