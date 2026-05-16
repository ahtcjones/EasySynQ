using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using EasySynQ.Domain;
using EasySynQ.Domain.Entities.Documents;
using EasySynQ.Services.Abstractions;
using EasySynQ.Services.Documents;
using EasySynQ.UI.Signing;

namespace EasySynQ.UI.Documents.ReviewAndSign;

/// <summary>
/// View model backing the C6b <see cref="ReviewAndSignDialog"/>.
/// Confirmation surface a named reviewer signs through:
/// resolves the signing role via <see cref="ISignatureRolePrompter"/>
/// when the dialog loads (multi-role users see the role picker;
/// single-role users auto-resolve), renders the confirmation
/// "Sign as reviewer of {Document} {Rev}? Signing as {role}.",
/// and on Sign calls
/// <see cref="IDocumentLifecycleService.SignAsReviewerAsync"/> +
/// queries the post-sign revision state so the caller (and tests)
/// can read whether this signature closed the assignment set
/// (revision now <see cref="DocumentLifecycle.Approved"/>) or left
/// the revision in <see cref="DocumentLifecycle.InReview"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Role resolved up-front.</b> Per the C6b plan §D, the
/// confirmation message bakes the resolved role in
/// ("Signing as {resolvedRole}"). Multi-role users therefore see
/// the role picker BEFORE the confirmation surface renders;
/// single-role users see only the confirmation. If the user
/// cancels the role picker, the confirmation dialog auto-closes
/// with a negative result (the surface has nothing to render
/// without a role).
/// </para>
/// <para>
/// <b>Post-sign transition state is surfaced.</b> After
/// <see cref="IDocumentLifecycleService.SignAsReviewerAsync"/>
/// returns, the VM re-loads the revision via
/// <see cref="IDocumentRevisionRepository.GetByIdAsync"/> and
/// exposes it on <see cref="PostSignRevision"/>. The detail VM
/// reads this after the dialog closes to refresh its own state;
/// tests assert on this property to distinguish the last-signer
/// path (revision now Approved) from the not-last-signer path
/// (revision still InReview).
/// </para>
/// </remarks>
public sealed partial class ReviewAndSignViewModel : ObservableObject
{
    private readonly ISignatureRolePrompter _rolePrompter;
    private readonly IDocumentLifecycleService _lifecycle;
    private readonly IDocumentRevisionRepository _revisions;
    private readonly Guid _revisionId;
    private readonly string _documentTitle;
    private readonly string _revisionLabel;
    private readonly Action<bool> _closeDialog;

    /// <summary>The role the prompter resolved for the current
    /// user, or <see langword="null"/> until the resolve command
    /// has completed (and not yet null after cancellation —
    /// cancellation auto-closes the dialog instead).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConfirmationMessage))]
    [NotifyPropertyChangedFor(nameof(IsRoleResolved))]
    [NotifyCanExecuteChangedFor(nameof(SignCommand))]
    public partial string? ResolvedRole { get; private set; }

    /// <summary>True once a role has been resolved — bound to the
    /// confirmation surface's visibility.</summary>
    public bool IsRoleResolved => !string.IsNullOrEmpty(ResolvedRole);

    /// <summary>True while the role prompter is running. Bound to
    /// a loading indicator in the dialog so multi-role users with
    /// a slow role-resolution step see feedback.</summary>
    [ObservableProperty]
    public partial bool IsResolvingRole { get; set; }

    /// <summary>Error message surfaced from a failed sign call.
    /// Null when no error.</summary>
    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    /// <summary>The post-sign revision state, populated after a
    /// successful sign. <see langword="null"/> before sign; the
    /// detail VM and tests read it after the dialog closes with a
    /// positive result.</summary>
    [ObservableProperty]
    public partial DocumentRevision? PostSignRevision { get; private set; }

    /// <summary>The confirmation message displayed once the role
    /// is resolved. Recomputed when <see cref="ResolvedRole"/>
    /// changes.</summary>
    public string ConfirmationMessage =>
        IsRoleResolved
            ? $"Sign as reviewer of {_documentTitle} {_revisionLabel}? Signing as {ResolvedRole}."
            : string.Empty;

    /// <summary>
    /// Constructs the view model bound to a specific InReview
    /// revision. The supplied dependencies are typically resolved
    /// by <see cref="ReviewAndSignPrompter"/> from the application's
    /// service provider.
    /// </summary>
    /// <param name="revisionId">Id of the InReview revision being
    /// signed. Must not be <see cref="Guid.Empty"/>.</param>
    /// <param name="documentTitle">Title of the parent Document
    /// for the confirmation message. Must not be null/empty/whitespace.</param>
    /// <param name="revisionLabel">Revision label for the
    /// confirmation message. Must not be null/empty/whitespace.</param>
    /// <param name="rolePrompter">Role-prompter for the
    /// multi-role signing path.</param>
    /// <param name="lifecycle">Lifecycle service for the actual
    /// sign transaction.</param>
    /// <param name="revisions">Revision repository for the
    /// post-sign state lookup.</param>
    /// <param name="closeDialog">Callback invoked with the dialog
    /// result on success (true) or cancel (false).</param>
    /// <exception cref="ArgumentException">Thrown when
    /// <paramref name="revisionId"/> is <see cref="Guid.Empty"/>
    /// or when <paramref name="documentTitle"/> /
    /// <paramref name="revisionLabel"/> is null/empty/whitespace.</exception>
    /// <exception cref="ArgumentNullException">Thrown when any
    /// dependency argument is <see langword="null"/>.</exception>
    public ReviewAndSignViewModel(
        Guid revisionId,
        string documentTitle,
        string revisionLabel,
        ISignatureRolePrompter rolePrompter,
        IDocumentLifecycleService lifecycle,
        IDocumentRevisionRepository revisions,
        Action<bool> closeDialog)
    {
        if (revisionId == Guid.Empty)
        {
            throw new ArgumentException(
                "RevisionId must not be Guid.Empty.", nameof(revisionId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(documentTitle);
        ArgumentException.ThrowIfNullOrWhiteSpace(revisionLabel);
        ArgumentNullException.ThrowIfNull(rolePrompter);
        ArgumentNullException.ThrowIfNull(lifecycle);
        ArgumentNullException.ThrowIfNull(revisions);
        ArgumentNullException.ThrowIfNull(closeDialog);

        _revisionId = revisionId;
        _documentTitle = documentTitle;
        _revisionLabel = revisionLabel;
        _rolePrompter = rolePrompter;
        _lifecycle = lifecycle;
        _revisions = revisions;
        _closeDialog = closeDialog;
    }

    /// <summary>
    /// Resolves the signing role via the role prompter (filtered
    /// on <see cref="PermissionNames.DocumentReview"/>). Triggered
    /// by the dialog's <c>Loaded</c> event. On cancel, auto-closes
    /// the dialog with a negative result — the confirmation
    /// surface has nothing to render without a resolved role.
    /// </summary>
    [RelayCommand]
    private async Task ResolveRoleAsync(CancellationToken cancellationToken)
    {
        ErrorMessage = null;
        IsResolvingRole = true;
        try
        {
            ResolvedRole = await _rolePrompter.ResolveSigningRoleAsync(
                PermissionNames.DocumentReview, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // User cancelled the role picker — auto-close the
            // confirmation dialog. No error message; cancellation
            // is a silent close.
            _closeDialog(false);
        }
#pragma warning disable CA1031 // Surface unexpected failures so the dialog isn't stuck in a broken state.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ErrorMessage = $"Could not resolve signing role: {ex.Message}";
        }
        finally
        {
            IsResolvingRole = false;
        }
    }

    /// <summary>
    /// Sign command — calls the lifecycle service with the
    /// resolved role; on success, re-loads the revision to surface
    /// its post-sign state (Approved if last signer; still
    /// InReview otherwise) and closes the dialog with a positive
    /// result. Disabled until a role has been resolved.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSign), AllowConcurrentExecutions = false)]
    private async Task SignAsync(CancellationToken cancellationToken)
    {
        ErrorMessage = null;
        try
        {
            await _lifecycle.SignAsReviewerAsync(
                _revisionId,
                ResolvedRole!,
                cancellationToken);

            // Re-load the revision to surface the post-sign
            // transition state. The detail VM will refresh
            // independently after dialog close; this property
            // primarily supports the test assertions
            // distinguishing last-signer vs not-last-signer paths.
            PostSignRevision = await _revisions.GetByIdAsync(
                _revisionId, cancellationToken);

            _closeDialog(true);
        }
#pragma warning disable CA1031 // Surface failures so the user sees what went wrong; the dialog stays open.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ErrorMessage = $"Signing failed: {ex.Message}";
        }
    }

    /// <summary>Cancel command — closes the dialog with a negative
    /// result.</summary>
    [RelayCommand]
    private void Cancel() => _closeDialog(false);

    private bool CanSign() => IsRoleResolved;
}
