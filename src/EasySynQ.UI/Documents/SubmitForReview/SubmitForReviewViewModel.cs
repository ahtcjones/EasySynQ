using System.Collections.Specialized;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using EasySynQ.Domain;
using EasySynQ.Services.Abstractions;
using EasySynQ.Services.Documents;
using EasySynQ.Services.Time;
using EasySynQ.UI.Documents.Reviewers;
using EasySynQ.UI.Signing;

namespace EasySynQ.UI.Documents.SubmitForReview;

/// <summary>
/// View model backing the C6b <see cref="SubmitForReviewDialog"/>.
/// Owns the full submit-for-review flow per the C6b plan §C: loads
/// the reviewer-candidate list, embeds the reviewer-picker VM
/// (<see cref="Picker"/>), invokes
/// <see cref="ISignatureRolePrompter"/> on submit to resolve the
/// author's signing role, and calls
/// <see cref="IDocumentLifecycleService.SubmitForReviewAsync"/> with
/// the selected reviewer ids + resolved role. Closes the dialog
/// with a positive result on success; leaves the dialog open on
/// role-picker cancel; surfaces other exceptions to
/// <see cref="ErrorMessage"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Notes field deferred.</b> The C6b plan §C dialog markup lists
/// an optional submit-notes field, but the
/// <see cref="IDocumentLifecycleService.SubmitForReviewAsync"/>
/// surface does not accept a notes parameter and the plan's
/// audit-row table keeps Submit at <c>2 + N</c>. Stop 3 omits the
/// notes field to avoid dead UI; the deferral is captured in
/// <c>docs/SCRATCHPAD.md</c>.
/// </para>
/// <para>
/// <b>Heavy VM, not a dumb data-capture.</b> Unlike the C6a
/// CreateDocument / EditMetadata dialog VMs (which are pure
/// data-capture and let the caller drive the service), this VM
/// owns the prompter + service-call flow per the explicit plan
/// shape. The
/// <see cref="EasySynQ.UI.Documents.SubmitForReview.SubmitForReviewPrompter"/>
/// wraps construction + <c>ShowDialog</c>; the detail VM consumes
/// the prompter and reads back only success/cancel.
/// </para>
/// <para>
/// <b>Role-picker cancel ≠ submit cancel.</b> When the user opens
/// the multi-role picker and clicks Cancel,
/// <see cref="ISignatureRolePrompter.ResolveSigningRoleAsync"/>
/// throws <see cref="OperationCanceledException"/>. The submit VM
/// catches it and leaves the dialog open so the user can revise
/// reviewer choice or close via the Cancel button.
/// </para>
/// </remarks>
public sealed partial class SubmitForReviewViewModel : ObservableObject
{
    private readonly IUserRepository _users;
    private readonly ISignatureRolePrompter _rolePrompter;
    private readonly IDocumentLifecycleService _lifecycle;
    private readonly IClock _clock;
    private readonly Guid _revisionId;
    private readonly Action<bool> _closeDialog;

    /// <summary>The reviewer-picker view model the dialog embeds.
    /// Starts empty; populated by
    /// <see cref="LoadCandidatesCommand"/> when the dialog opens.</summary>
    [ObservableProperty]
    public partial ReviewerPickerViewModel Picker { get; private set; }

    /// <summary>True while
    /// <see cref="LoadCandidatesCommand"/> or the loaded picker is
    /// still being built. Bound to a busy indicator in the dialog.</summary>
    [ObservableProperty]
    public partial bool IsLoadingCandidates { get; set; }

    /// <summary>Error message surfaced from a failed submit (not a
    /// role-picker cancel — that path is a silent no-op).</summary>
    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    /// <summary>
    /// Constructs the view model bound to a specific Draft revision.
    /// The supplied dependencies are typically resolved by
    /// <see cref="SubmitForReviewPrompter"/> from the application's
    /// service provider.
    /// </summary>
    /// <param name="revisionId">Id of the Draft revision being
    /// submitted. Must not be <see cref="Guid.Empty"/>.</param>
    /// <param name="users">User-repository surface for the
    /// reviewer-candidate load. Must not be
    /// <see langword="null"/>.</param>
    /// <param name="rolePrompter">Role-prompter for the multi-role
    /// signing path. Must not be <see langword="null"/>.</param>
    /// <param name="lifecycle">Lifecycle service for the actual
    /// submit transaction. Must not be <see langword="null"/>.</param>
    /// <param name="clock">Clock for the <c>asOfUtc</c> at which to
    /// resolve effective permissions. Must not be
    /// <see langword="null"/>.</param>
    /// <param name="closeDialog">Callback invoked with the dialog
    /// result on success (true) or cancel (false). Must not be
    /// <see langword="null"/>.</param>
    /// <exception cref="ArgumentException">Thrown when
    /// <paramref name="revisionId"/> is <see cref="Guid.Empty"/>.</exception>
    /// <exception cref="ArgumentNullException">Thrown when any
    /// other argument is <see langword="null"/>.</exception>
    public SubmitForReviewViewModel(
        Guid revisionId,
        IUserRepository users,
        ISignatureRolePrompter rolePrompter,
        IDocumentLifecycleService lifecycle,
        IClock clock,
        Action<bool> closeDialog)
    {
        if (revisionId == Guid.Empty)
        {
            throw new ArgumentException(
                "RevisionId must not be Guid.Empty.", nameof(revisionId));
        }

        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(rolePrompter);
        ArgumentNullException.ThrowIfNull(lifecycle);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(closeDialog);

        _revisionId = revisionId;
        _users = users;
        _rolePrompter = rolePrompter;
        _lifecycle = lifecycle;
        _clock = clock;
        _closeDialog = closeDialog;

        Picker = new ReviewerPickerViewModel(Array.Empty<ReviewerCandidate>());
        Picker.SelectedCandidates.CollectionChanged += OnSelectionChanged;
    }

    /// <summary>
    /// Loads the reviewer-candidate list from the user repository
    /// (filtered to users holding
    /// <see cref="PermissionNames.DocumentReview"/> at the current
    /// instant) and replaces <see cref="Picker"/> with a fresh
    /// picker VM over the loaded candidates. Surfaces failures to
    /// <see cref="ErrorMessage"/>.
    /// </summary>
    [RelayCommand]
    private async Task LoadCandidatesAsync(CancellationToken cancellationToken)
    {
        ErrorMessage = null;
        IsLoadingCandidates = true;
        try
        {
            var users = await _users.GetUsersWithPermissionAsync(
                PermissionNames.DocumentReview,
                _clock.UtcNow,
                cancellationToken);

            var candidates = users
                .Select(u => new ReviewerCandidate(u.Id, u.DisplayName, u.Username))
                .ToList();

            // Swap the picker; unsubscribe the old picker's
            // CollectionChanged and subscribe to the new picker's
            // so SubmitCommand.CanExecute stays accurate.
            Picker.SelectedCandidates.CollectionChanged -= OnSelectionChanged;
            Picker = new ReviewerPickerViewModel(candidates);
            Picker.SelectedCandidates.CollectionChanged += OnSelectionChanged;
            SubmitCommand.NotifyCanExecuteChanged();
        }
        catch (OperationCanceledException)
        {
            // Caller cancelled — dialog will close via its own path;
            // no error message.
            throw;
        }
#pragma warning disable CA1031 // Surface any load failure to the user; the dialog stays open so they can retry or cancel.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ErrorMessage = $"Loading reviewers failed: {ex.Message}";
        }
        finally
        {
            IsLoadingCandidates = false;
        }
    }

    /// <summary>
    /// Submit command — runs the role-prompter, calls the lifecycle
    /// service's submit transaction, closes the dialog on success.
    /// Disabled until at least one reviewer is selected per ADR
    /// 0008 (the submit transaction requires a non-empty reviewer
    /// set).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSubmit), AllowConcurrentExecutions = false)]
    private async Task SubmitAsync(CancellationToken cancellationToken)
    {
        ErrorMessage = null;
        try
        {
            // Step 1: resolve which role the author is signing the
            // submission as. Multi-role users see the picker;
            // single-role users auto-pick.
            var role = await _rolePrompter.ResolveSigningRoleAsync(
                PermissionNames.DocumentSubmitForReview,
                cancellationToken);

            // Step 2: call the lifecycle service. Reviewer ids
            // pulled from Picker.SelectedCandidates in input order.
            var reviewerIds = Picker.SelectedCandidates
                .Select(c => c.Id)
                .ToList();

            await _lifecycle.SubmitForReviewAsync(
                _revisionId,
                reviewerIds,
                effectiveFromUtc: null,
                signingAsRole: role,
                cancellationToken);

            _closeDialog(true);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Role-picker dialog cancelled by user. Leave the
            // submit dialog open so they can pick different
            // reviewers or close via the explicit Cancel button.
            // We deliberately do NOT surface an error message;
            // cancellation is a silent no-op.
        }
#pragma warning disable CA1031 // Surface failures (permission, service-level invariant) so the user sees what went wrong.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ErrorMessage = $"Submission failed: {ex.Message}";
        }
    }

    /// <summary>Cancel command — closes the dialog with a negative
    /// result.</summary>
    [RelayCommand]
    private void Cancel() => _closeDialog(false);

    private bool CanSubmit() => Picker.SelectedCandidates.Count >= 1;

    private void OnSelectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => SubmitCommand.NotifyCanExecuteChanged();
}
