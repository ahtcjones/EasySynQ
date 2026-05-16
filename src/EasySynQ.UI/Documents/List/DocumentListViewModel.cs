using System.IO;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using EasySynQ.Domain;
using EasySynQ.Domain.Entities.Documents;
using EasySynQ.Services.Abstractions;
using EasySynQ.Services.Documents;
using EasySynQ.Services.Time;
using EasySynQ.UI.Documents.CreateDocument;
using EasySynQ.UI.Documents.Detail;
using EasySynQ.UI.Navigation;

namespace EasySynQ.UI.Documents.List;

/// <summary>
/// View model for the Documents list view (ADR 0008 C6a). Populates
/// from <see cref="IDocumentRepository.GetNonRetiredAsync"/>, projects
/// each document + its latest revision + author username into a
/// <see cref="DocumentListItem"/> row, and exposes the
/// Create-new-document command. Drives the detail view via the
/// injected factory delegate.
/// </summary>
/// <remarks>
/// <para>
/// <b>Refresh model.</b> The list reloads in full after a Create
/// completes — there is no incremental insert. For C6a's pilot scale
/// (single-digit-to-dozens of documents), the full reload is cheap
/// and avoids domain-event indirection for a single consumer per
/// the plan's B4 decision.
/// </para>
/// <para>
/// <b>Author username resolution.</b> The list does a single bulk
/// fetch via <see cref="IUserRepository.GetByIdsAsync"/> to project
/// usernames into rows — one extra query per refresh, no N+1.
/// Authors whose User row is missing or soft-deleted display as
/// <c>"(unknown)"</c>; this preserves audit-log integrity while
/// keeping the row renderable.
/// </para>
/// </remarks>
public sealed partial class DocumentListViewModel : ObservableObject, IDirtyStateAware
{
    private const string UnknownAuthorDisplay = "(unknown)";

    private readonly IDocumentRepository _documents;
    private readonly IDocumentRevisionRepository _revisions;
    private readonly IUserRepository _users;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly ICreateDocumentPrompter _createPrompter;
    private readonly IDocumentLifecycleService _lifecycle;
    private readonly IClock _clock;
    private readonly Func<Document, DocumentDetailViewModel> _detailFactory;

    /// <summary>Constructs the view model over its dependencies.</summary>
    public DocumentListViewModel(
        IDocumentRepository documents,
        IDocumentRevisionRepository revisions,
        IUserRepository users,
        ICurrentUserAccessor currentUser,
        ICreateDocumentPrompter createPrompter,
        IDocumentLifecycleService lifecycle,
        IClock clock,
        Func<Document, DocumentDetailViewModel> detailFactory)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(revisions);
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(createPrompter);
        ArgumentNullException.ThrowIfNull(lifecycle);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(detailFactory);

        _documents = documents;
        _revisions = revisions;
        _users = users;
        _currentUser = currentUser;
        _createPrompter = createPrompter;
        _lifecycle = lifecycle;
        _clock = clock;
        _detailFactory = detailFactory;
    }

    /// <summary>The current list snapshot. Replaced wholesale on each
    /// refresh.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<DocumentListItem> Items { get; private set; } = [];

    /// <summary>
    /// The currently-selected row, or <see langword="null"/> when no
    /// selection. Selection drives <see cref="DetailViewModel"/>
    /// construction via the injected factory.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailViewModel))]
    public partial DocumentListItem? SelectedItem { get; set; }

    private DocumentDetailViewModel? _detailViewModel;

    /// <summary>
    /// The detail view model for the currently-selected row, or
    /// <see langword="null"/> when no selection. Recomputed lazily
    /// when <see cref="SelectedItem"/> changes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Event subscription discipline.</b> The detail VM raises
    /// <see cref="DocumentDetailViewModel.Deleted"/> after a successful
    /// Hard-Delete; the list subscribes here so it can clear
    /// <see cref="SelectedItem"/> and re-run <see cref="LoadAsync"/>
    /// before the user can act on the now-stale row. The matching
    /// unsubscribe runs whenever the cached VM is replaced or cleared
    /// (selection change to a different row, or selection cleared).
    /// Skipping the unsubscribe would leak the handler reference and
    /// — worse — leave the orphaned VM holding the list VM alive
    /// through the event-handler closure across navigation. Pair both
    /// halves; never sub without unsub.
    /// </para>
    /// </remarks>
    public DocumentDetailViewModel? DetailViewModel
    {
        get
        {
            if (SelectedItem is null)
            {
                ClearDetailViewModel();
                return null;
            }

            // Cache so back-to-back binding reads don't reconstruct.
            // Recompute when the selected item's DocumentId has
            // changed (or no cached value exists).
            if (_detailViewModel is null
                || _detailViewModel.Document.Id != SelectedItem.DocumentId)
            {
                ClearDetailViewModel();

                // The list row carries a snapshot; rebuild a Document
                // entity stub to hand to the factory. The detail VM's
                // own LoadAsync will re-fetch the live row + latest
                // revision from the repository for its own view.
                var stub = new Document(
                    id: SelectedItem.DocumentId,
                    number: SelectedItem.Number,
                    title: SelectedItem.Title);
                _detailViewModel = _detailFactory(stub);
                _detailViewModel.Deleted += OnDetailVmDeletedAsync;
            }
            return _detailViewModel;
        }
    }

    private void ClearDetailViewModel()
    {
        if (_detailViewModel is not null)
        {
            _detailViewModel.Deleted -= OnDetailVmDeletedAsync;
            _detailViewModel = null;
        }
    }

    private async void OnDetailVmDeletedAsync(object? sender, EventArgs e)
    {
        // After a successful Hard-Delete the document and its sole
        // revision are gone from the DB. Clearing SelectedItem first
        // triggers the DetailViewModel getter's ClearDetailViewModel
        // path (unsubscribing this handler and dropping the cached
        // VM), then LoadAsync refreshes Items so the now-orphaned row
        // disappears from the list. Without this wiring the user
        // could click Hard-Delete a second time on the stale row and
        // get a KeyNotFoundException (Document already gone) — the
        // bug Finding 3 in C6a smoke surfaced.
        SelectedItem = null;
        await LoadAsync(CancellationToken.None);
    }

    /// <summary>
    /// Loads the list from the repository, projecting each row through
    /// the latest-revision + author-username resolution. Replaces the
    /// existing <see cref="Items"/> wholesale.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    [RelayCommand]
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        var docs = await _documents.GetNonRetiredAsync(cancellationToken);
        if (docs.Count == 0)
        {
            Items = [];
            return;
        }

        // Latest revision per document — one query per row.
        // Acceptable for C6a's pilot scale; revisit if list cardinality
        // grows enough to justify a single batched query.
        var rows = new List<(Document Doc, DocumentRevision? Latest)>(docs.Count);
        foreach (var d in docs)
        {
            var latest = await _revisions.GetLatestRevisionAsync(d.Id, cancellationToken);
            rows.Add((d, latest));
        }

        // Bulk-fetch authors. Distinct ids — same user often authors
        // many documents.
        var authorIds = rows
            .Where(r => r.Latest is not null)
            .Select(r => r.Latest!.AuthorUserId)
            .Distinct()
            .ToList();

        Dictionary<Guid, string> users;
        if (authorIds.Count == 0)
        {
            users = [];
        }
        else
        {
            var fetched = await _users.GetByIdsAsync(authorIds, cancellationToken);
            users = fetched.ToDictionary(u => u.Id, u => u.Username);
        }

        var asOf = _clock.UtcNow;
        Items = rows
            .Select(row => ProjectRow(row.Doc, row.Latest, users, asOf))
            .ToList();
    }

    private static DocumentListItem ProjectRow(
        Document doc,
        DocumentRevision? latest,
        Dictionary<Guid, string> users,
        DateTime asOf)
    {
        if (latest is null)
        {
            // Defensive — a Document without revisions cannot be
            // produced by C6a's CreateDocumentAsync (which writes
            // both rows in one transaction). Still, render something
            // rather than throwing or hiding the row.
            return new DocumentListItem(
                DocumentId: doc.Id,
                Number: doc.Number,
                Title: doc.Title,
                CurrentRevisionLabel: string.Empty,
                Lifecycle: Domain.Enums.DocumentLifecycle.Draft,
                LifecycleDisplay: string.Empty,
                AuthorDisplay: UnknownAuthorDisplay);
        }

        var author = users.TryGetValue(latest.AuthorUserId, out var name)
            ? name
            : UnknownAuthorDisplay;

        return new DocumentListItem(
            DocumentId: doc.Id,
            Number: doc.Number,
            Title: doc.Title,
            CurrentRevisionLabel: latest.RevisionLabel,
            Lifecycle: latest.Lifecycle,
            LifecycleDisplay: DocumentLifecycleDisplay.Format(
                latest.Lifecycle, latest.EffectiveFromUtc, asOf),
            AuthorDisplay: author);
    }

    /// <summary>
    /// Computed property mirroring the current user's
    /// <c>Document.Create</c> permission. Drives
    /// <see cref="CreateCommand"/>'s CanExecute. Exposed publicly so
    /// the view can hide the affordance entirely when false (per the
    /// C6a no-stubs rule, the Create button does not render in a
    /// disabled state for users who lack the permission).
    /// </summary>
    public bool CanCreate =>
        _currentUser.Permissions.Contains(PermissionNames.DocumentCreate);

    /// <summary>
    /// Create-document command — gated on
    /// <see cref="PermissionNames.DocumentCreate"/>. Invokes the
    /// prompter; on OK, creates the Document (and optionally attaches
    /// the picked PDF in a second transaction) via the lifecycle
    /// service, then re-runs <see cref="LoadAsync"/> to refresh the
    /// list.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    [RelayCommand(CanExecute = nameof(CanCreate))]
    public async Task CreateAsync(CancellationToken cancellationToken)
    {
        var result = await _createPrompter.PromptAsync(cancellationToken);
        if (result is null)
        {
            return; // user cancelled
        }

        var created = await _lifecycle.CreateDocumentAsync(
            result.Number, result.Title, cancellationToken);

        if (result.SelectedPdfPath is not null)
        {
            // Need the new Draft revision's id to attach the PDF —
            // fetch via the repository (CreateDocumentAsync returned
            // the Document; the revision id is on the latest-revision
            // lookup).
            var latest = await _revisions.GetLatestRevisionAsync(
                created.Id, cancellationToken);
            if (latest is not null)
            {
                await using var stream = File.OpenRead(result.SelectedPdfPath);
                await _lifecycle.AttachPdfToDraftAsync(
                    latest.Id,
                    stream,
                    Path.GetFileName(result.SelectedPdfPath),
                    cancellationToken);
            }
        }

        await LoadAsync(cancellationToken);
    }

    /// <inheritdoc />
    public bool HasUnsavedChanges => false;

    /// <inheritdoc />
    public Task<bool> ConfirmDiscardAsync(CancellationToken cancellationToken)
        => Task.FromResult(true);
}
