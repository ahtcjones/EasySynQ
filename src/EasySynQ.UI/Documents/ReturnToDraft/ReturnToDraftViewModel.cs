using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using EasySynQ.Services.Documents;

namespace EasySynQ.UI.Documents.ReturnToDraft;

/// <summary>
/// View model backing the C6b <see cref="ReturnToDraftDialog"/>.
/// Captures the required reason text, calls
/// <see cref="IDocumentLifecycleService.ReturnToDraftAsync"/> with
/// the revision id + reason, and closes the dialog on success.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not a signed transition.</b> Plan §E describes a
/// role-prompter on this dialog, but the audit-row table keeps
/// ReturnToDraft at <c>1 + N</c> (revision Update + N assignment
/// Updates — no Signature Insert) and ADR 0008's "Signatures
/// reset" section describes only the in-progress reviewer
/// signatures being discarded, not a new signature being staged
/// for the return transition itself. The stop-5 plan-vs-service
/// reconciliation (user-confirmed) is to follow the audit-row
/// table and the C3 implementation — the dialog has the required-
/// reason textarea and Return/Cancel; no role prompter is
/// invoked.
/// </para>
/// <para>
/// <b>Reason persisted on the revision.</b> The reason is stamped
/// onto <c>DocumentRevision.LastReturnToDraftReason</c> by the
/// service (stop 1 schema addition). When the revision lands back
/// in Draft, the author sees it in the UI; the audit log's
/// revision-Update row captures it in its <c>After</c> snapshot.
/// On the next submit-for-review, <c>Submit</c> clears the live
/// value (the audit log retains the historical trail).
/// </para>
/// </remarks>
public sealed partial class ReturnToDraftViewModel : ObservableObject
{
    private readonly IDocumentLifecycleService _lifecycle;
    private readonly Guid _revisionId;
    private readonly Action<bool> _closeDialog;

    /// <summary>Reason text the reviewer supplies. Required —
    /// non-whitespace gates the Return command's
    /// CanExecute.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReturnCommand))]
    public partial string Reason { get; set; } = string.Empty;

    /// <summary>Error message surfaced from a failed return call.
    /// Null when no error.</summary>
    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    /// <summary>
    /// Constructs the view model bound to a specific InReview
    /// revision.
    /// </summary>
    /// <param name="revisionId">Id of the InReview revision being
    /// returned to Draft. Must not be <see cref="Guid.Empty"/>.</param>
    /// <param name="lifecycle">Lifecycle service for the return
    /// transaction. Must not be <see langword="null"/>.</param>
    /// <param name="closeDialog">Callback invoked with the dialog
    /// result on success (true) or cancel (false). Must not be
    /// <see langword="null"/>.</param>
    /// <exception cref="ArgumentException">Thrown when
    /// <paramref name="revisionId"/> is <see cref="Guid.Empty"/>.</exception>
    /// <exception cref="ArgumentNullException">Thrown when any
    /// other argument is <see langword="null"/>.</exception>
    public ReturnToDraftViewModel(
        Guid revisionId,
        IDocumentLifecycleService lifecycle,
        Action<bool> closeDialog)
    {
        if (revisionId == Guid.Empty)
        {
            throw new ArgumentException(
                "RevisionId must not be Guid.Empty.", nameof(revisionId));
        }

        ArgumentNullException.ThrowIfNull(lifecycle);
        ArgumentNullException.ThrowIfNull(closeDialog);

        _revisionId = revisionId;
        _lifecycle = lifecycle;
        _closeDialog = closeDialog;
    }

    /// <summary>
    /// Return command — calls the lifecycle service to transition
    /// the revision back to Draft, stamping
    /// <see cref="EasySynQ.Domain.Entities.Documents.DocumentRevision.LastReturnToDraftReason"/>
    /// with the supplied reason. Disabled until
    /// <see cref="Reason"/> is non-whitespace.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanReturn), AllowConcurrentExecutions = false)]
    private async Task ReturnAsync(CancellationToken cancellationToken)
    {
        ErrorMessage = null;
        try
        {
            await _lifecycle.ReturnToDraftAsync(_revisionId, Reason, cancellationToken);
            _closeDialog(true);
        }
#pragma warning disable CA1031 // Surface failures so the user sees what went wrong; the dialog stays open.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ErrorMessage = $"Return to draft failed: {ex.Message}";
        }
    }

    /// <summary>Cancel command — closes the dialog with a negative
    /// result.</summary>
    [RelayCommand]
    private void Cancel() => _closeDialog(false);

    private bool CanReturn() => !string.IsNullOrWhiteSpace(Reason);
}
