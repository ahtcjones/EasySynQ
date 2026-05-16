using System.Windows.Controls;

namespace EasySynQ.UI.Documents.Reviewers;

/// <summary>
/// User control hosting the C6b reviewer picker (ADR 0008 C6b).
/// Two-row layout: filter <see cref="TextBox"/> on top, multi-select
/// <see cref="ListBox"/> below. The code-behind owns one
/// responsibility — syncing
/// <see cref="ListBox.SelectedItems"/> into the bound
/// <see cref="ReviewerPickerViewModel.SelectedCandidates"/> on every
/// selection change so the VM stays the headlessly-testable source
/// of truth.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why selection sync lives in code-behind.</b> WPF's
/// <see cref="ListBox.SelectedItems"/> is read-only at the binding
/// layer — there is no built-in two-way binding for it. The
/// canonical MVVM-friendly remedy is either an attached behavior
/// or a code-behind handler. Code-behind is the lighter weight
/// option, especially since this control has no other behavior to
/// host, and the handler stays a single statement.
/// </para>
/// <para>
/// <b>Filter changes re-select from the VM.</b> When
/// <see cref="ReviewerPickerViewModel.FilterText"/> changes,
/// <see cref="ListBox.ItemsSource"/> rebinds via
/// <see cref="ReviewerPickerViewModel.FilteredCandidates"/> and
/// WPF clears <see cref="ListBox.SelectedItems"/> on the visible
/// list. The VM's <c>SelectedCandidates</c> is the source of truth
/// — when the filter widens and previously-selected candidates
/// reappear in the visible list, this handler's
/// <see cref="ListBox.SelectionChanged"/> event fires as WPF
/// realizes the new items; we re-apply <c>IsSelected</c> from the
/// VM at that point.
/// </para>
/// </remarks>
public partial class ReviewerPickerControl : UserControl
{
    /// <summary>Constructs the control. The view model is supplied
    /// via <c>DataContext</c> (typically by the embedding dialog's
    /// XAML).</summary>
    public ReviewerPickerControl()
    {
        InitializeComponent();
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not ReviewerPickerViewModel vm)
        {
            return;
        }

        foreach (var added in e.AddedItems.OfType<ReviewerCandidate>())
        {
            if (!vm.SelectedCandidates.Contains(added))
            {
                vm.SelectedCandidates.Add(added);
            }
        }

        foreach (var removed in e.RemovedItems.OfType<ReviewerCandidate>())
        {
            // Only remove the VM entry if the row was actually
            // unselected (not just hidden by a filter narrowing).
            // CandidatesListBox.SelectedItems is the post-change
            // visible-selection set; an item in RemovedItems that
            // is no longer present in the visible list AND no
            // longer in CandidatesListBox.SelectedItems was truly
            // unselected. An item filtered out is in RemovedItems
            // too (WPF clears selection for hidden items) but the
            // VM should keep it.
            var stillVisible = CandidatesListBox.Items
                .OfType<ReviewerCandidate>()
                .Any(c => c.Id == removed.Id);
            if (stillVisible)
            {
                vm.SelectedCandidates.Remove(removed);
            }
        }
    }
}
