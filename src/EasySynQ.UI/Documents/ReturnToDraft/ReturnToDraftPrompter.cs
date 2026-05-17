using System.Windows;
using System.Windows.Interop;

using EasySynQ.Services.Documents;

namespace EasySynQ.UI.Documents.ReturnToDraft;

/// <summary>
/// Production <see cref="IReturnToDraftPrompter"/>. Shows
/// <see cref="ReturnToDraftDialog"/> modally; returns the boolean
/// dialog result. Mirrors the stop 3
/// <c>SubmitForReviewPrompter</c> / stop 4
/// <c>ReviewAndSignPrompter</c> shape — singleton lifetime,
/// constructs a fresh Window per call, attaches the application's
/// main window as owner when one is available.
/// </summary>
public sealed class ReturnToDraftPrompter : IReturnToDraftPrompter
{
    private readonly IDocumentLifecycleService _lifecycle;

    /// <summary>Constructs the prompter over the lifecycle service
    /// dependency.</summary>
    public ReturnToDraftPrompter(IDocumentLifecycleService lifecycle)
    {
        ArgumentNullException.ThrowIfNull(lifecycle);
        _lifecycle = lifecycle;
    }

    /// <inheritdoc />
    public Task<bool> PromptAsync(Guid revisionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var dialog = new ReturnToDraftDialog(revisionId, _lifecycle);

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
        return Task.FromResult(ok == true);
    }
}
