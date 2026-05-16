using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using EasySynQ.Domain;
using EasySynQ.Services.Abstractions;
using EasySynQ.Services.Documents;

namespace EasySynQ.UI.Documents.Comments;

/// <summary>
/// View model backing the C6b <see cref="CommentPanelControl"/>.
/// Loads the chronological comment thread for a revision, resolves
/// each comment's author to a display name, and offers an Add
/// textarea + button gated on
/// <see cref="PermissionNames.DocumentReview"/> + non-whitespace
/// text.
/// </summary>
/// <remarks>
/// <para>
/// <b>Order: oldest first.</b> Comments render in chronological
/// forward order (ascending <see cref="CommentRow.CreatedAtUtc"/>)
/// so the panel reads like a discussion thread. Reverse-chronological
/// would suit an activity-feed shape; the in-review comment
/// surface is a thread, not a feed.
/// </para>
/// <para>
/// <b>InReview gate is parent-controlled.</b> The panel is hosted
/// by the detail view only when <c>Lifecycle == InReview</c>; the
/// VM itself doesn't re-check. The service-level state check in
/// <see cref="IDocumentLifecycleService.AddCommentAsync"/> is the
/// safety net for the race-condition corner case (revision
/// transitions out of InReview while a comment is being typed).
/// </para>
/// <para>
/// <b>Author resolution is batched on load.</b> A single
/// <see cref="IUserRepository.GetByIdsAsync"/> call resolves every
/// distinct <c>AuthorUserId</c> in the loaded comment set; missing
/// (soft-deleted) users fall back to <c>"(unknown user)"</c> so
/// the panel renders coherently even when the historical-author
/// link is severed.
/// </para>
/// </remarks>
public sealed partial class CommentPanelViewModel : ObservableObject
{
    private readonly IDocumentReviewCommentRepository _comments;
    private readonly IUserRepository _users;
    private readonly IDocumentLifecycleService _lifecycle;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly Guid _revisionId;

    /// <summary>The loaded comment thread, oldest first. The Add
    /// command refreshes this collection after a successful
    /// insert.</summary>
    public ObservableCollection<CommentRow> Comments { get; } = [];

    /// <summary>Free-form text the user is composing. Two-way bound
    /// to the panel's textarea; non-whitespace gates
    /// <see cref="AddCommand"/>.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    public partial string NewCommentText { get; set; } = string.Empty;

    /// <summary>True while a load or add operation is in flight.
    /// Bound to a loading indicator (and disables both
    /// commands).</summary>
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>Error message surfaced from a failed load or add.
    /// Null when no error.</summary>
    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    /// <summary>True when the current user holds
    /// <see cref="PermissionNames.DocumentReview"/>; bound to the
    /// Add affordance's visibility so commenters with no
    /// permission see the read-only thread.</summary>
    public bool CanComment =>
        _currentUser.Permissions.Contains(PermissionNames.DocumentReview);

    /// <summary>
    /// Constructs the panel view model bound to a specific InReview
    /// revision.
    /// </summary>
    /// <param name="revisionId">Id of the revision whose comment
    /// thread to load. Must not be <see cref="Guid.Empty"/>.</param>
    /// <param name="comments">Comment-repository surface.</param>
    /// <param name="users">User-repository surface for the author
    /// resolution.</param>
    /// <param name="lifecycle">Lifecycle service for the
    /// <c>AddCommentAsync</c> call.</param>
    /// <param name="currentUser">Current-user accessor for the
    /// permission check.</param>
    /// <exception cref="ArgumentException">Thrown when
    /// <paramref name="revisionId"/> is <see cref="Guid.Empty"/>.</exception>
    /// <exception cref="ArgumentNullException">Thrown when any
    /// other argument is <see langword="null"/>.</exception>
    public CommentPanelViewModel(
        Guid revisionId,
        IDocumentReviewCommentRepository comments,
        IUserRepository users,
        IDocumentLifecycleService lifecycle,
        ICurrentUserAccessor currentUser)
    {
        if (revisionId == Guid.Empty)
        {
            throw new ArgumentException(
                "RevisionId must not be Guid.Empty.", nameof(revisionId));
        }

        ArgumentNullException.ThrowIfNull(comments);
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(lifecycle);
        ArgumentNullException.ThrowIfNull(currentUser);

        _revisionId = revisionId;
        _comments = comments;
        _users = users;
        _lifecycle = lifecycle;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Loads (or reloads) the comment thread. Replaces the
    /// <see cref="Comments"/> collection with the freshly-projected
    /// row set in chronological forward order.
    /// </summary>
    [RelayCommand]
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        ErrorMessage = null;
        IsBusy = true;
        try
        {
            var rawComments = await _comments.GetByRevisionIdAsync(
                _revisionId, cancellationToken);

            // Batch-resolve author display names so the rendered
            // rows are self-contained.
            var authorIds = rawComments
                .Select(c => c.AuthorUserId)
                .Distinct()
                .ToList();
            var authors = await _users.GetByIdsAsync(authorIds, cancellationToken);
            var authorById = authors.ToDictionary(u => u.Id);

            var rows = rawComments
                .OrderBy(c => c.CreatedAtUtc)
                .Select(c =>
                {
                    var (display, username) = authorById.TryGetValue(c.AuthorUserId, out var u)
                        ? (u.DisplayName, (string?)u.Username)
                        : ("(unknown user)", (string?)null);
                    return new CommentRow(
                        c.Id, display, username, c.BodyText, c.CreatedAtUtc);
                })
                .ToList();

            Comments.Clear();
            foreach (var row in rows)
            {
                Comments.Add(row);
            }
        }
#pragma warning disable CA1031 // Surface load failures so the user sees them; the panel still functions for the Add path.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ErrorMessage = $"Loading comments failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Add command — calls the lifecycle service to insert a new
    /// comment, then reloads the thread to surface it (along with
    /// any concurrent additions by other reviewers since load).
    /// Disabled when the user lacks
    /// <see cref="PermissionNames.DocumentReview"/> or when
    /// <see cref="NewCommentText"/> is whitespace.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAdd), AllowConcurrentExecutions = false)]
    private async Task AddAsync(CancellationToken cancellationToken)
    {
        ErrorMessage = null;
        IsBusy = true;
        try
        {
            await _lifecycle.AddCommentAsync(
                _revisionId, NewCommentText, cancellationToken);

            NewCommentText = string.Empty;
        }
#pragma warning disable CA1031 // Surface add failures so the user sees them; the panel stays open.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ErrorMessage = $"Adding comment failed: {ex.Message}";
            return;
        }
        finally
        {
            IsBusy = false;
        }

        // Refresh outside the try/finally so a refresh failure
        // doesn't mask the successful insert.
        await LoadAsync(cancellationToken);
    }

    private bool CanAdd() =>
        CanComment && !string.IsNullOrWhiteSpace(NewCommentText);
}
