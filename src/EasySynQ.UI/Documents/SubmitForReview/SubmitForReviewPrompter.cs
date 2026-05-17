using System.Windows;
using System.Windows.Interop;

using EasySynQ.Services.Abstractions;
using EasySynQ.Services.Documents;
using EasySynQ.Services.Time;
using EasySynQ.UI.Signing;

namespace EasySynQ.UI.Documents.SubmitForReview;

/// <summary>
/// Production <see cref="ISubmitForReviewPrompter"/>. Shows
/// <see cref="SubmitForReviewDialog"/> modally; returns the boolean
/// dialog result. Mirrors the C6a <c>CreateDocumentPrompter</c> /
/// <c>EditMetadataPrompter</c> shape — singleton lifetime,
/// constructs a fresh Window per call (Windows are
/// single-<c>ShowDialog</c>), attaches the application's main
/// window as owner when one is available.
/// </summary>
/// <remarks>
/// Unlike the C6a dumb-VM prompters, this prompter forwards the
/// service-tier dependencies into the dialog VM, which performs the
/// candidate load + role-prompter + submit transaction internally
/// (per the C6b plan §C explicit shape).
/// </remarks>
public sealed class SubmitForReviewPrompter : ISubmitForReviewPrompter
{
    private readonly IUserRepository _users;
    private readonly ISignatureRolePrompter _rolePrompter;
    private readonly IDocumentLifecycleService _lifecycle;
    private readonly IClock _clock;

    /// <summary>Constructs the prompter over its scoped service
    /// dependencies.</summary>
    public SubmitForReviewPrompter(
        IUserRepository users,
        ISignatureRolePrompter rolePrompter,
        IDocumentLifecycleService lifecycle,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(rolePrompter);
        ArgumentNullException.ThrowIfNull(lifecycle);
        ArgumentNullException.ThrowIfNull(clock);

        _users = users;
        _rolePrompter = rolePrompter;
        _lifecycle = lifecycle;
        _clock = clock;
    }

    /// <inheritdoc />
    public Task<bool> PromptAsync(Guid revisionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var dialog = new SubmitForReviewDialog(
            revisionId, _users, _rolePrompter, _lifecycle, _clock);

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
