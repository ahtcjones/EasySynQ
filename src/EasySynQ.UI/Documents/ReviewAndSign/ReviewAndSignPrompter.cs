using System.Windows;

using EasySynQ.Services.Abstractions;
using EasySynQ.Services.Documents;
using EasySynQ.UI.Signing;

namespace EasySynQ.UI.Documents.ReviewAndSign;

/// <summary>
/// Production <see cref="IReviewAndSignPrompter"/>. Shows
/// <see cref="ReviewAndSignDialog"/> modally; returns the boolean
/// dialog result. Mirrors the stop 3
/// <c>SubmitForReviewPrompter</c> shape — singleton lifetime,
/// constructs a fresh Window per call, attaches the application's
/// main window as owner when one is available.
/// </summary>
public sealed class ReviewAndSignPrompter : IReviewAndSignPrompter
{
    private readonly ISignatureRolePrompter _rolePrompter;
    private readonly IDocumentLifecycleService _lifecycle;
    private readonly IDocumentRevisionRepository _revisions;

    /// <summary>Constructs the prompter over its scoped service
    /// dependencies.</summary>
    public ReviewAndSignPrompter(
        ISignatureRolePrompter rolePrompter,
        IDocumentLifecycleService lifecycle,
        IDocumentRevisionRepository revisions)
    {
        ArgumentNullException.ThrowIfNull(rolePrompter);
        ArgumentNullException.ThrowIfNull(lifecycle);
        ArgumentNullException.ThrowIfNull(revisions);

        _rolePrompter = rolePrompter;
        _lifecycle = lifecycle;
        _revisions = revisions;
    }

    /// <inheritdoc />
    public Task<bool> PromptAsync(
        Guid revisionId,
        string documentTitle,
        string revisionLabel,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var dialog = new ReviewAndSignDialog(
            revisionId,
            documentTitle,
            revisionLabel,
            _rolePrompter,
            _lifecycle,
            _revisions);

        if (Application.Current?.MainWindow is { } owner && !ReferenceEquals(owner, dialog))
        {
            dialog.Owner = owner;
        }

        var ok = dialog.ShowDialog();
        return Task.FromResult(ok == true);
    }
}
