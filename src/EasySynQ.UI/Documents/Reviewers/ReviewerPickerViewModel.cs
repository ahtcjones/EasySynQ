using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

namespace EasySynQ.UI.Documents.Reviewers;

/// <summary>
/// View model backing the C6b <see cref="ReviewerPickerControl"/>.
/// Holds the immutable candidate list, a mutable filter text, and an
/// <see cref="ObservableCollection{T}"/> of selected reviewers. The
/// computed <see cref="FilteredCandidates"/> property is the
/// <c>ListBox.ItemsSource</c> the picker control renders.
/// </summary>
/// <remarks>
/// <para>
/// <b>Selection ownership.</b> The control's <see cref="ListBox"/>
/// runs in <c>SelectionMode="Multiple"</c> and is the selection
/// authority — its code-behind syncs
/// <see cref="ListBox.SelectedItems"/> into
/// <see cref="SelectedCandidates"/> on every selection change so
/// the VM stays authoritative for headless tests and so the
/// embedding dialog (stop 3) reads selection from the VM, not from
/// the WPF surface. <see cref="SelectedCandidates"/> is exposed
/// writable so tests can drive selection directly.
/// </para>
/// <para>
/// <b>Filter semantics.</b> Substring match against
/// <see cref="ReviewerCandidate.DisplayName"/> OR
/// <see cref="ReviewerCandidate.Username"/>, case-insensitive
/// (<see cref="StringComparison.OrdinalIgnoreCase"/>). An empty or
/// whitespace <see cref="FilterText"/> returns every candidate.
/// Filter order preserves <see cref="Candidates"/>' input order so
/// the picker is deterministic across sessions.
/// </para>
/// <para>
/// <b>FilteredCandidates is recomputed on access.</b> A fresh
/// IEnumerable enumeration runs each time
/// <see cref="FilteredCandidates"/> is read. The candidate set is
/// small (eligible users in the deployment, capped by the
/// permission set) so per-keystroke filtering is cheap. Selection
/// persistence across filter changes is the control's
/// responsibility, not the VM's — when filter narrows and a
/// previously-selected row disappears from the visible list,
/// <see cref="SelectedCandidates"/> still contains it; when filter
/// widens, the control re-applies <see cref="ListBoxItem.IsSelected"/>
/// from the VM's selected set.
/// </para>
/// </remarks>
public sealed partial class ReviewerPickerViewModel : ObservableObject
{
    /// <summary>The candidate list the picker renders, in supplied
    /// order. Constructor input; immutable thereafter.</summary>
    public IReadOnlyList<ReviewerCandidate> Candidates { get; }

    /// <summary>The currently-selected reviewers. The picker
    /// control's code-behind syncs this to
    /// <see cref="ListBox.SelectedItems"/>; tests may mutate
    /// directly. <see cref="ObservableCollection{T}"/> rather than a
    /// snapshot list so the embedding dialog can react to selection
    /// changes via <see cref="ObservableCollection{T}.CollectionChanged"/>
    /// (used by SubmitForReviewViewModel's OK-can-execute gate at
    /// stop 3).</summary>
    public ObservableCollection<ReviewerCandidate> SelectedCandidates { get; }

    /// <summary>The current filter text. Two-way bound to the
    /// picker control's filter <c>TextBox</c>; triggers
    /// <see cref="FilteredCandidates"/> recomputation via
    /// <c>NotifyPropertyChangedFor</c>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredCandidates))]
    public partial string FilterText { get; set; } = string.Empty;

    /// <summary>
    /// <see cref="Candidates"/> filtered by <see cref="FilterText"/>.
    /// Empty/whitespace filter returns every candidate. Substring
    /// match (case-insensitive) on <see cref="ReviewerCandidate.DisplayName"/>
    /// or <see cref="ReviewerCandidate.Username"/>. Input order
    /// preserved.
    /// </summary>
    public IEnumerable<ReviewerCandidate> FilteredCandidates
    {
        get
        {
            if (string.IsNullOrWhiteSpace(FilterText))
            {
                return Candidates;
            }

            var needle = FilterText;
            return Candidates.Where(c =>
                c.DisplayName.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                c.Username.Contains(needle, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Constructs the picker view model over the supplied candidate
    /// list. <see cref="SelectedCandidates"/> starts empty;
    /// <see cref="FilterText"/> starts empty.
    /// </summary>
    /// <param name="candidates">Active reviewer-eligible users
    /// (typically the result of
    /// <see cref="EasySynQ.Services.Abstractions.IUserRepository.GetUsersWithPermissionAsync"/>
    /// projected to <see cref="ReviewerCandidate"/>). Must not be
    /// <see langword="null"/>. Empty is allowed (renders an empty
    /// list — the embedding dialog handles the empty-eligible-users
    /// case at the OK-can-execute level).</param>
    /// <exception cref="ArgumentNullException">Thrown when
    /// <paramref name="candidates"/> is <see langword="null"/>.</exception>
    public ReviewerPickerViewModel(IReadOnlyList<ReviewerCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        Candidates = candidates;
        SelectedCandidates = [];
    }
}
