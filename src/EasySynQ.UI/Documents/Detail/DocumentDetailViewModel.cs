using System.IO;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using EasySynQ.Domain;
using EasySynQ.Domain.Entities.Documents;
using EasySynQ.Domain.Enums;
using EasySynQ.Services.Abstractions;
using EasySynQ.Services.Documents;
using EasySynQ.Services.Time;
using EasySynQ.Services.Vault;
using EasySynQ.UI.Documents.Comments;
using EasySynQ.UI.Documents.Controls;
using EasySynQ.UI.Documents.EditMetadata;
using EasySynQ.UI.Documents.ReturnToDraft;
using EasySynQ.UI.Documents.ReviewAndSign;
using EasySynQ.UI.Documents.SubmitForReview;
using EasySynQ.UI.Navigation;

namespace EasySynQ.UI.Documents.Detail;

/// <summary>
/// View model for the Document detail view (ADR 0008 C6a). Displays
/// the current revision's metadata, exposes the embedded PDF
/// viewer's <c>DocumentPath</c> binding, and surfaces the C6a
/// author-working-alone affordances (Edit Metadata, Replace PDF,
/// Hard-Delete Draft) gated by the brief's affordance matrix.
/// </summary>
/// <remarks>
/// <para>
/// <b>No "coming soon" stubs.</b> Per the C6a brief, only affordances
/// whose service methods exist appear. Submit-for-Review,
/// Review-and-Sign, Retire, etc. land with C6b / C7+ and are absent
/// from this view's exposed commands.
/// </para>
/// <para>
/// <b>Gap-window rendering.</b> When the latest revision is Approved
/// but its <c>EffectiveFromUtc</c> is in the future,
/// <see cref="LifecycleDisplay"/> surfaces
/// <c>"Approved (effective YYYY-MM-DD)"</c> per the C3 handoff's
/// "stored state is the source of truth" pattern. C6a's own gestures
/// never produce this state — but the renderer honors it honestly
/// when fixture-planted or when C6b's submission flow eventually
/// lands.
/// </para>
/// </remarks>
public sealed partial class DocumentDetailViewModel : ObservableObject, IDirtyStateAware
{
    private readonly IDocumentRepository _documents;
    private readonly IDocumentRevisionRepository _revisions;
    private readonly IDocumentReviewAssignmentRepository _assignments;
    private readonly IDocumentReviewCommentRepository _comments;
    private readonly IUserRepository _users;
    private readonly IVaultService _vault;
    private readonly IVaultPathProvider _pathProvider;
    private readonly IClock _clock;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IDocumentLifecycleService _lifecycle;
    private readonly IFilePicker _filePicker;
    private readonly IEditMetadataPrompter _editPrompter;
    private readonly ISubmitForReviewPrompter _submitPrompter;
    private readonly IReviewAndSignPrompter _signPrompter;
    private readonly IReturnToDraftPrompter _returnPrompter;

    /// <summary>
    /// Constructs the view model bound to a specific <see cref="Document"/>.
    /// The Document is supplied at construction time (typically by
    /// <c>DocumentListViewModel.DetailViewModel</c> via the injected
    /// factory delegate); <see cref="CurrentRevision"/> and
    /// <see cref="VaultDocumentUrl"/> populate when
    /// <see cref="LoadAsync"/> runs.
    /// </summary>
    public DocumentDetailViewModel(
        Document document,
        IDocumentRepository documents,
        IDocumentRevisionRepository revisions,
        IDocumentReviewAssignmentRepository assignments,
        IDocumentReviewCommentRepository comments,
        IUserRepository users,
        IVaultService vault,
        IVaultPathProvider pathProvider,
        IClock clock,
        ICurrentUserAccessor currentUser,
        IDocumentLifecycleService lifecycle,
        IFilePicker filePicker,
        IEditMetadataPrompter editPrompter,
        ISubmitForReviewPrompter submitPrompter,
        IReviewAndSignPrompter signPrompter,
        IReturnToDraftPrompter returnPrompter)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(revisions);
        ArgumentNullException.ThrowIfNull(assignments);
        ArgumentNullException.ThrowIfNull(comments);
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(pathProvider);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(lifecycle);
        ArgumentNullException.ThrowIfNull(filePicker);
        ArgumentNullException.ThrowIfNull(editPrompter);
        ArgumentNullException.ThrowIfNull(submitPrompter);
        ArgumentNullException.ThrowIfNull(signPrompter);
        ArgumentNullException.ThrowIfNull(returnPrompter);

        Document = document;
        _documents = documents;
        _revisions = revisions;
        _assignments = assignments;
        _comments = comments;
        _users = users;
        _vault = vault;
        _pathProvider = pathProvider;
        _clock = clock;
        _currentUser = currentUser;
        _lifecycle = lifecycle;
        _filePicker = filePicker;
        _editPrompter = editPrompter;
        _submitPrompter = submitPrompter;
        _signPrompter = signPrompter;
        _returnPrompter = returnPrompter;
    }

    /// <summary>The Document this detail view is bound to.</summary>
    public Document Document { get; private set; }

    /// <summary>
    /// The latest revision of <see cref="Document"/>, or
    /// <see langword="null"/> until <see cref="LoadAsync"/> has run
    /// (or if the Document has no revisions, which should never occur
    /// for a Document produced by C6a's CreateDocumentAsync).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentRevisionLabel))]
    [NotifyPropertyChangedFor(nameof(LifecycleDisplay))]
    [NotifyPropertyChangedFor(nameof(VaultDocumentUrl))]
    [NotifyPropertyChangedFor(nameof(CanEditMetadata))]
    [NotifyPropertyChangedFor(nameof(CanReplacePdf))]
    [NotifyPropertyChangedFor(nameof(CanHardDelete))]
    [NotifyPropertyChangedFor(nameof(CanSubmitForReview))]
    [NotifyPropertyChangedFor(nameof(CanReviewAndSign))]
    [NotifyPropertyChangedFor(nameof(CanReturnToDraft))]
    [NotifyPropertyChangedFor(nameof(ShowAssignmentPanel))]
    [NotifyPropertyChangedFor(nameof(ShowCommentPanel))]
    [NotifyPropertyChangedFor(nameof(LastReturnToDraftReason))]
    [NotifyPropertyChangedFor(nameof(HasLastReturnToDraftReason))]
    [NotifyCanExecuteChangedFor(nameof(EditMetadataCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReplacePdfCommand))]
    [NotifyCanExecuteChangedFor(nameof(HardDeleteDraftCommand))]
    [NotifyCanExecuteChangedFor(nameof(SubmitForReviewCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReviewAndSignCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReturnToDraftCommand))]
    public partial DocumentRevision? CurrentRevision { get; private set; }

    /// <summary>
    /// Assigned-reviewer rows for the current revision when
    /// <see cref="DocumentLifecycle.InReview"/>. Empty otherwise.
    /// Populated by <see cref="LoadAsync"/>; rendered by the
    /// detail view's assignment panel with a per-status badge
    /// (Pending = amber, Signed = green, Discarded = neutral).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanReviewAndSign))]
    [NotifyCanExecuteChangedFor(nameof(ReviewAndSignCommand))]
    public partial IReadOnlyList<AssignedReviewerRow> Assignments { get; private set; } =
        Array.Empty<AssignedReviewerRow>();

    /// <summary>
    /// Comment-panel view model when the current revision is in
    /// <see cref="DocumentLifecycle.InReview"/>;
    /// <see langword="null"/> otherwise. Built fresh per
    /// <see cref="LoadAsync"/>; the detail view's
    /// <c>ContentControl</c> swaps the embedded
    /// <c>CommentPanelControl</c> when this property changes.
    /// </summary>
    [ObservableProperty]
    public partial CommentPanelViewModel? CommentPanel { get; private set; }

    /// <summary>
    /// Content-virtual-host URL for the current revision's PDF, or
    /// <see langword="null"/> when no PDF is attached. Bound to
    /// <c>PdfViewerControl.DocumentPath</c>. The control resolves
    /// the URL through its content-virtual-host mapping at
    /// <see cref="ContentRoot"/>.
    /// </summary>
    /// <remarks>
    /// Per ADR 0010's 2026-05-16 amendment, the viewer cannot
    /// directly navigate to a <c>file://</c> URL because Chromium's
    /// same-origin policy blocks PDF.js's PDF fetch across two
    /// <c>file://</c> origins (the viewer.html and the target PDF).
    /// The VM resolves the on-disk vault path via
    /// <see cref="IVaultService.GetVaultFilePathAsync"/> then
    /// translates to a virtual-host URL via
    /// <see cref="PdfViewerControl.BuildContentUrl"/>.
    /// </remarks>
    [ObservableProperty]
    public partial string? VaultDocumentUrl { get; private set; }

    /// <summary>
    /// Absolute path to the folder mapped to the content virtual host.
    /// Bound to <c>PdfViewerControl.ContentRoot</c>. The VM exposes
    /// the vault root from <see cref="IVaultPathProvider"/> so the
    /// view's control knows where to register the host mapping.
    /// </summary>
    public string ContentRoot => _pathProvider.VaultRoot;

    /// <summary>
    /// <see langword="true"/> when the embedded viewer raised
    /// <see cref="PdfViewerControl.NavigationFailed"/> and the failure
    /// hasn't been cleared by a subsequent successful load. The view
    /// binds a banner's visibility to this so PDF-load failures are
    /// user-visible instead of silently appearing as a black page.
    /// </summary>
    [ObservableProperty]
    public partial bool HasViewerLoadError { get; private set; }

    /// <summary>
    /// Human-readable message for the viewer-load-error banner.
    /// Combines the failure reason (from the control's event args)
    /// with the attempted path when one was supplied. Empty when
    /// <see cref="HasViewerLoadError"/> is <see langword="false"/>.
    /// </summary>
    [ObservableProperty]
    public partial string ViewerErrorMessage { get; private set; } = string.Empty;

    /// <summary>Display string for the current revision's label, or
    /// empty when no revision.</summary>
    public string CurrentRevisionLabel => CurrentRevision?.RevisionLabel ?? string.Empty;

    /// <summary>
    /// Human-readable lifecycle string. Uses
    /// <see cref="DocumentLifecycleDisplay"/> so the gap-window
    /// rendering matches the list view.
    /// </summary>
    public string LifecycleDisplay => CurrentRevision is null
        ? string.Empty
        : DocumentLifecycleDisplay.Format(
            CurrentRevision.Lifecycle,
            CurrentRevision.EffectiveFromUtc,
            _clock.UtcNow);

    private bool IsLatestDraft => CurrentRevision?.Lifecycle == DocumentLifecycle.Draft;

    private bool IsLatestInReview => CurrentRevision?.Lifecycle == DocumentLifecycle.InReview;

    private bool IsAuthorOfLatest =>
        CurrentRevision is not null
        && _currentUser.UserId is { } me
        && CurrentRevision.AuthorUserId == me;

    /// <summary>
    /// True when the current user is in the assigned-reviewer list
    /// for the current revision with a <c>Pending</c> status. Used
    /// to gate the Review-and-Sign affordance — only named
    /// reviewers with an in-flight assignment can sign.
    /// </summary>
    private bool IsPendingNamedReviewer
    {
        get
        {
            if (_currentUser.UserId is not { } me)
            {
                return false;
            }
            return Assignments.Any(a =>
                a.Status == DocumentReviewAssignmentStatus.Pending
                && AssignmentReviewerId(a.AssignmentId) == me);
        }
    }

    // The AssignedReviewerRow projection drops the ReviewerUserId
    // for display, so we look up the underlying assignment row's
    // ReviewerUserId via the stored DocumentReviewAssignment list
    // captured during the most recent LoadAsync. Cached in a
    // private dictionary keyed by AssignmentId.
    private Dictionary<Guid, Guid> _assignmentReviewers = [];

    private Guid? AssignmentReviewerId(Guid assignmentId)
        => _assignmentReviewers.TryGetValue(assignmentId, out var userId) ? userId : null;

    /// <summary>
    /// Whether the Edit Metadata affordance is enabled — latest
    /// revision is Draft and the user holds
    /// <see cref="PermissionNames.DocumentEditDraft"/>.
    /// </summary>
    public bool CanEditMetadata =>
        IsLatestDraft
        && _currentUser.Permissions.Contains(PermissionNames.DocumentEditDraft);

    /// <summary>
    /// Whether the Replace PDF affordance is enabled — latest
    /// revision is Draft and the user holds
    /// <see cref="PermissionNames.DocumentEditDraft"/>.
    /// </summary>
    public bool CanReplacePdf =>
        IsLatestDraft
        && _currentUser.Permissions.Contains(PermissionNames.DocumentEditDraft);

    /// <summary>
    /// Whether the Hard-Delete Draft affordance is enabled — latest
    /// revision is Draft, current user authored that revision, and
    /// the user holds <see cref="PermissionNames.DocumentHardDelete"/>.
    /// </summary>
    public bool CanHardDelete =>
        IsLatestDraft
        && IsAuthorOfLatest
        && _currentUser.Permissions.Contains(PermissionNames.DocumentHardDelete);

    /// <summary>
    /// Whether the Submit-for-Review affordance is enabled — latest
    /// revision is Draft and the user holds both
    /// <see cref="PermissionNames.DocumentSubmitForReview"/> AND
    /// <see cref="PermissionNames.DocumentAssignReviewers"/>. The
    /// AND of both per ADR 0008's strict-gatekeeper option: users
    /// with only one of the two permissions see no submit
    /// affordance.
    /// </summary>
    public bool CanSubmitForReview =>
        IsLatestDraft
        && _currentUser.Permissions.Contains(PermissionNames.DocumentSubmitForReview)
        && _currentUser.Permissions.Contains(PermissionNames.DocumentAssignReviewers);

    /// <summary>
    /// Whether the Review-and-Sign affordance is enabled — latest
    /// revision is in InReview, the current user is in the
    /// assigned-reviewer list with a Pending assignment, and the
    /// user holds <see cref="PermissionNames.DocumentReview"/>.
    /// </summary>
    public bool CanReviewAndSign =>
        IsLatestInReview
        && IsPendingNamedReviewer
        && _currentUser.Permissions.Contains(PermissionNames.DocumentReview);

    /// <summary>
    /// Whether the Return-to-Draft affordance is enabled — latest
    /// revision is in InReview and the user holds
    /// <see cref="PermissionNames.DocumentReturnForEdits"/>. Per the
    /// C6b plan §G, available to reviewers and to the author when
    /// their permissions allow it; no separate gating for the
    /// author vs reviewer path.
    /// </summary>
    public bool CanReturnToDraft =>
        IsLatestInReview
        && _currentUser.Permissions.Contains(PermissionNames.DocumentReturnForEdits);

    /// <summary>True when the detail view should render the
    /// assigned-reviewer panel — the panel is meaningful only while
    /// the revision is InReview.</summary>
    public bool ShowAssignmentPanel => IsLatestInReview;

    /// <summary>True when the detail view should render the
    /// reviewer-comment panel — same lifecycle gate as
    /// <see cref="ShowAssignmentPanel"/>.</summary>
    public bool ShowCommentPanel => IsLatestInReview;

    /// <summary>
    /// The most recent return-to-draft reason stamped on the
    /// current revision, or <see langword="null"/> when the
    /// revision has never been returned (or has since been
    /// re-submitted, which clears the live column). The detail
    /// view surfaces this when the revision is back in Draft so
    /// the author sees why their submission was returned.
    /// </summary>
    public string? LastReturnToDraftReason => CurrentRevision?.LastReturnToDraftReason;

    /// <summary>True when <see cref="LastReturnToDraftReason"/> is
    /// non-empty — bound to the reason-display visibility.</summary>
    public bool HasLastReturnToDraftReason =>
        !string.IsNullOrEmpty(LastReturnToDraftReason);

    /// <summary>
    /// Loads (or reloads) <see cref="CurrentRevision"/> and
    /// <see cref="VaultDocumentUrl"/> from the repositories. Called on
    /// initial view show and after each mutating command completes.
    /// Also clears any prior <see cref="HasViewerLoadError"/> state —
    /// a fresh load is the natural moment to retire a stale error.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Layer 0 banner coverage (smoke walk #5 verification finding).</b>
    /// Vault-side I/O (notably
    /// <see cref="IVaultService.GetVaultFilePathAsync"/>) can throw
    /// <see cref="FileNotFoundException"/> /
    /// <see cref="InvalidDataException"/> /
    /// <see cref="KeyNotFoundException"/> when the on-disk vault file
    /// is missing, corrupted, or the blob row is unexpectedly gone.
    /// Without this catch the exception escapes through the async
    /// void handler to the dispatcher last-resort handler, which is
    /// the wrong shape — the user gets a generic "Something went
    /// wrong" dialog instead of the contextual viewer banner that
    /// already exists for sub-resource failures. Routing the
    /// exception through <see cref="OnViewerNavigationFailed"/> uses
    /// the same banner mechanism the WebView2-side failures use,
    /// keeping the user-visible failure surface consistent across
    /// all four layers of the cascade-init model (Layer 0
    /// VM-side / Layer 1 outer-navigation / Layer 2 sub-resource /
    /// Layer 3 JS-internal).
    /// </para>
    /// <para>
    /// Catch is narrow to <see cref="IOException"/> and
    /// <see cref="InvalidDataException"/> — the failure modes the
    /// vault contract documents. Other exceptions (a corrupted
    /// SQLite read, a programming error in this method, etc.) still
    /// escape to the dispatcher because they are NOT vault-content
    /// failures and the banner's "Failed to load PDF" framing would
    /// mislead. <see cref="KeyNotFoundException"/> is also caught
    /// because <see cref="IVaultService.GetVaultFilePathAsync"/>
    /// throws it when a blob row is missing — same user-visible
    /// failure-mode bucket.
    /// </para>
    /// </remarks>
    [RelayCommand]
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        // Clearing the viewer error first means a subsequent re-fire
        // of NavigationFailed (e.g., the new content also fails)
        // surfaces fresh, not stale-then-fresh.
        HasViewerLoadError = false;
        ViewerErrorMessage = string.Empty;

        // Re-fetch the Document so any since-changed metadata
        // (Number/Title from EditMetadata) is reflected.
        var fresh = await _documents.GetByIdAsync(Document.Id, cancellationToken);
        if (fresh is not null)
        {
            Document = fresh;
            OnPropertyChanged(nameof(Document));
        }

        CurrentRevision = await _revisions.GetLatestRevisionAsync(
            Document.Id, cancellationToken);

        if (CurrentRevision is { VaultBlobId: { } blobId })
        {
            try
            {
                var filePath = await _vault.GetVaultFilePathAsync(blobId, cancellationToken);
                VaultDocumentUrl = PdfViewerControl.BuildContentUrl(filePath, ContentRoot);
            }
            catch (Exception ex) when (
                ex is FileNotFoundException ||
                ex is InvalidDataException ||
                ex is KeyNotFoundException)
            {
                // Route the vault-side failure through the same banner
                // wire the WebView2-side failures use. Clear the URL
                // so the viewer doesn't try to render a stale path.
                VaultDocumentUrl = null;
                OnViewerNavigationFailed(
                    reason: $"VaultFileUnavailable ({ex.GetType().Name})",
                    attemptedPath: blobId.ToString());
            }
        }
        else
        {
            VaultDocumentUrl = null;
        }

        // C6b: load assignment + comment surfaces when the revision
        // is InReview; clear them otherwise. Assignment panel
        // requires user-display resolution; comment panel is its
        // own VM and self-loads on its Loaded event.
        if (CurrentRevision is { Lifecycle: DocumentLifecycle.InReview, Id: var revId })
        {
            var rawAssignments = await _assignments.GetByRevisionIdAsync(
                revId, cancellationToken);

            var reviewerIds = rawAssignments
                .Select(a => a.ReviewerUserId)
                .Distinct()
                .ToList();
            var reviewers = await _users.GetByIdsAsync(reviewerIds, cancellationToken);
            var reviewerById = reviewers.ToDictionary(u => u.Id);

            Assignments = rawAssignments
                .OrderBy(a => a.AssignedAtUtc)
                .Select(a =>
                {
                    var (display, username) = reviewerById.TryGetValue(a.ReviewerUserId, out var u)
                        ? (u.DisplayName, (string?)u.Username)
                        : ("(unknown user)", (string?)null);
                    return new AssignedReviewerRow(a.Id, display, username, a.Status);
                })
                .ToList();

            _assignmentReviewers = rawAssignments.ToDictionary(a => a.Id, a => a.ReviewerUserId);

            // Build a fresh comment-panel VM so its CanComment + load
            // reflect the current user's permissions and the current
            // revision. CommentPanelControl.OnLoaded triggers its
            // LoadCommand to populate the thread.
            CommentPanel = new CommentPanelViewModel(
                revId, _comments, _users, _lifecycle, _currentUser);

            OnPropertyChanged(nameof(IsPendingNamedReviewer));
            OnPropertyChanged(nameof(CanReviewAndSign));
            ReviewAndSignCommand.NotifyCanExecuteChanged();
        }
        else
        {
            Assignments = Array.Empty<AssignedReviewerRow>();
            _assignmentReviewers = new Dictionary<Guid, Guid>();
            CommentPanel = null;
        }
    }

    /// <summary>
    /// Handler the host view forwards
    /// <see cref="PdfViewerControl.NavigationFailed"/> events into.
    /// Populates <see cref="HasViewerLoadError"/> and
    /// <see cref="ViewerErrorMessage"/> so the banner surfaces in the
    /// UI. The next call to <see cref="LoadAsync"/> clears the state.
    /// </summary>
    /// <param name="reason">Failure reason from the viewer control's
    /// event args (e.g., <c>"NavigationCompletedWithError"</c>,
    /// <c>"WebView2InitFailed"</c>).</param>
    /// <param name="attemptedPath">The path/URL that was being
    /// navigated to when the failure occurred, or
    /// <see langword="null"/> for failures unbound to a specific
    /// target.</param>
    public void OnViewerNavigationFailed(string reason, string? attemptedPath)
    {
        HasViewerLoadError = true;
        ViewerErrorMessage = string.IsNullOrEmpty(attemptedPath)
            ? $"Failed to load PDF ({reason}). See the application log for details."
            : $"Failed to load PDF ({reason}). See the application log for details. " +
              $"Attempted: {attemptedPath}";
    }

    /// <summary>
    /// Edit Metadata command — opens the
    /// <see cref="IEditMetadataPrompter"/>, calls
    /// <see cref="IDocumentLifecycleService.EditDraftMetadataAsync"/>
    /// on OK, then reloads.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanEditMetadata))]
    private async Task EditMetadataAsync(CancellationToken cancellationToken)
    {
        var result = await _editPrompter.PromptAsync(
            Document.Number, Document.Title, cancellationToken);
        if (result is null)
        {
            return;
        }

        await _lifecycle.EditDraftMetadataAsync(
            Document.Id, result.NewNumber, result.NewTitle, cancellationToken);

        await LoadAsync(cancellationToken);
    }

    /// <summary>
    /// Replace PDF command — opens the file picker, calls
    /// <see cref="IDocumentLifecycleService.AttachPdfToDraftAsync"/>
    /// on the latest revision, then reloads. Skips silently if the
    /// user cancels the picker or there is no current revision.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanReplacePdf))]
    private async Task ReplacePdfAsync(CancellationToken cancellationToken)
    {
        if (CurrentRevision is null)
        {
            return;
        }

        var path = _filePicker.PickFile(
            "Select PDF document",
            "PDF documents (*.pdf)|*.pdf");
        if (path is null)
        {
            return;
        }

        await using var stream = File.OpenRead(path);
        await _lifecycle.AttachPdfToDraftAsync(
            CurrentRevision.Id, stream, Path.GetFileName(path), cancellationToken);

        await LoadAsync(cancellationToken);
    }

    /// <summary>
    /// Hard-Delete Draft command — calls
    /// <see cref="IDocumentLifecycleService.HardDeleteDraftAsync"/>.
    /// The detail VM does NOT reload after a successful delete; the
    /// caller (list view) is expected to refresh and clear the
    /// selection. <see cref="Deleted"/> fires so the list can react.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanHardDelete))]
    private async Task HardDeleteDraftAsync(CancellationToken cancellationToken)
    {
        await _lifecycle.HardDeleteDraftAsync(Document.Id, cancellationToken);
        Deleted?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Submit-for-Review command (ADR 0008 C6b stop 7) — opens
    /// <see cref="ISubmitForReviewPrompter"/>; the prompter's
    /// dialog VM owns the candidate load + role-prompter + submit
    /// transaction internally. On a positive return, reloads to
    /// surface the new InReview state + assignment list +
    /// comment panel.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSubmitForReview))]
    private async Task SubmitForReviewAsync(CancellationToken cancellationToken)
    {
        if (CurrentRevision is null)
        {
            return;
        }

        var submitted = await _submitPrompter.PromptAsync(
            CurrentRevision.Id, cancellationToken);
        if (!submitted)
        {
            return;
        }

        await LoadAsync(cancellationToken);
    }

    /// <summary>
    /// Review-and-Sign command (ADR 0008 C6b stop 7) — opens
    /// <see cref="IReviewAndSignPrompter"/>; the prompter resolves
    /// the role then calls the lifecycle service's sign transaction.
    /// On success, reloads — if this signature completed the
    /// assignment set the revision is now Approved and the panel
    /// rendering changes accordingly.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanReviewAndSign))]
    private async Task ReviewAndSignAsync(CancellationToken cancellationToken)
    {
        if (CurrentRevision is null)
        {
            return;
        }

        var signed = await _signPrompter.PromptAsync(
            CurrentRevision.Id,
            Document.Title,
            CurrentRevision.RevisionLabel,
            cancellationToken);
        if (!signed)
        {
            return;
        }

        await LoadAsync(cancellationToken);
    }

    /// <summary>
    /// Return-to-Draft command (ADR 0008 C6b stop 7) — opens
    /// <see cref="IReturnToDraftPrompter"/>; the prompter's dialog
    /// captures the required reason and calls the lifecycle
    /// service. On success, reloads — the revision is back in Draft
    /// with assignments Discarded and
    /// <see cref="LastReturnToDraftReason"/> stamped.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanReturnToDraft))]
    private async Task ReturnToDraftAsync(CancellationToken cancellationToken)
    {
        if (CurrentRevision is null)
        {
            return;
        }

        var returned = await _returnPrompter.PromptAsync(
            CurrentRevision.Id, cancellationToken);
        if (!returned)
        {
            return;
        }

        await LoadAsync(cancellationToken);
    }

    /// <summary>
    /// Raised after a successful HardDeleteDraft so the list view can
    /// refresh and clear the selection. Fire-and-forget; the detail
    /// VM does not re-load itself after delete (the underlying rows
    /// are gone).
    /// </summary>
    public event EventHandler? Deleted;

    /// <inheritdoc />
    public bool HasUnsavedChanges => false;

    /// <inheritdoc />
    public Task<bool> ConfirmDiscardAsync(CancellationToken cancellationToken)
        => Task.FromResult(true);
}
