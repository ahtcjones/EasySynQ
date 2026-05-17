using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

using EasySynQ.UI.Documents.Controls;

namespace EasySynQ.UI.Documents.Detail;

/// <summary>
/// Code-behind for <see cref="DocumentDetailView"/>. Triggers
/// <see cref="DocumentDetailViewModel.LoadCommand"/> when the view is
/// first attached and again on every reattach (the list view rebuilds
/// the detail VM whenever the selected row changes, so each fresh
/// detail VM gets its own load). Forwards
/// <see cref="PdfViewerControl.NavigationFailed"/> into the VM so
/// PDF-load failures surface in the banner instead of as a silent
/// black page (C6a smoke walk #2 Finding 3b).
/// </summary>
public partial class DocumentDetailView : UserControl
{
    // Tracks which VM instance we've already dispatched LoadCommand
    // for during the current attach cycle. WPF's ContentControl +
    // DataTemplate ordering is non-deterministic across rebinds: on
    // some second-and-later selections the view's Loaded event fires
    // BEFORE the DataContext binding evaluates, and OnLoadedAsync sees
    // a null DataContext and no-ops — leaving the new VM unloaded and
    // the affordance row blank until the user navigates away and back
    // (C6b stop 9 follow-up). DataContextChanged is the fallback
    // signal: whichever event fires last with both signals satisfied
    // dispatches the load. The guard ensures exactly one dispatch per
    // (view, VM) pair regardless of fire order.
    private DocumentDetailViewModel? _loadDispatchedFor;

    /// <summary>Parameterless constructor for XAML instantiation.</summary>
    public DocumentDetailView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChangedAsync;
    }

    private async void OnLoadedAsync(object sender, RoutedEventArgs e)
    {
        await TryDispatchLoadAsync();
    }

    private async void OnDataContextChangedAsync(
        object sender,
        DependencyPropertyChangedEventArgs e)
    {
        // The new DataContext is on its own attach cycle — clear any
        // stale dispatch record so the new VM gets a load. (The cached
        // reference would not match anyway since each list-row change
        // mints a fresh DocumentDetailViewModel via the factory, but
        // explicit-clear is cheaper than relying on inequality.)
        if (!ReferenceEquals(e.NewValue, _loadDispatchedFor))
        {
            _loadDispatchedFor = null;
        }
        await TryDispatchLoadAsync();
    }

    private async Task TryDispatchLoadAsync()
    {
        // Both signals must be satisfied before LoadCommand fires:
        //   - The view is in the visual tree (IsLoaded), so async
        //     property updates from LoadAsync have a UI to bind to.
        //   - The DataContext is a DocumentDetailViewModel.
        //   - We have not already dispatched for this VM instance.
        //   - LoadCommand reports CanExecute.
        // The early-return order is cheapest-first; the
        // ReferenceEquals guard is the load-once invariant.
        if (!IsLoaded
            || DataContext is not DocumentDetailViewModel vm
            || ReferenceEquals(vm, _loadDispatchedFor)
            || !vm.LoadCommand.CanExecute(null))
        {
            return;
        }

        _loadDispatchedFor = vm;
        await vm.LoadCommand.ExecuteAsync(null);
    }

    private void OnViewerNavigationFailed(
        object? sender,
        PdfViewerNavigationFailedEventArgs e)
    {
        // The control raised NavigationFailed (e.g., WebView2 init
        // failure, viewer-assets missing, navigation completed with
        // error). Forward to the VM so the red banner above the
        // viewer surfaces the reason. Per ADR 0010 amendment §"Banner
        // semantics", the message format and state lifetime are the
        // VM's concern; this code-behind is the routing seam only.
        if (DataContext is DocumentDetailViewModel vm)
        {
            vm.OnViewerNavigationFailed(e.Reason, e.AttemptedPath);
        }
    }
}
