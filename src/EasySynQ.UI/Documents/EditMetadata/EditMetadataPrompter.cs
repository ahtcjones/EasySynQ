using System.Windows;
using System.Windows.Interop;

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
        // PresentationSource guard — Application.MainWindow can be a
        // closed or never-shown Window after the sign-in flow; Owner
        // assignment on such a Window throws.
        if (Application.Current?.MainWindow is { } owner
            && !ReferenceEquals(owner, dialog)
            && PresentationSource.FromVisual(owner) is HwndSource)
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
