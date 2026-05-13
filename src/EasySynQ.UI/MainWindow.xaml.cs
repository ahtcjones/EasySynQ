using System.Windows;
using System.Windows.Data;

using EasySynQ.UI.Navigation;

namespace EasySynQ.UI;

/// <summary>
/// Application shell. Hosts the topbar, navigation tree, and content
/// area. Code-behind is intentionally minimal — all shell logic lives
/// in <see cref="MainShellViewModel"/>. The handful of responsibilities
/// retained here is documented per-method.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainShellViewModel _viewModel;

    /// <summary>
    /// DI-friendly constructor. <see cref="App.OnStartup"/> constructs
    /// the view model and invokes this overload directly; once Chunk E5
    /// lands the full host, that path becomes a service-provider resolve
    /// rather than a manual <c>new</c>.
    /// </summary>
    /// <param name="viewModel">The shell view model.</param>
    /// <exception cref="ArgumentNullException">Thrown when
    /// <paramref name="viewModel"/> is <see langword="null"/>.</exception>
    public MainWindow(MainShellViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        // Bindings inside a ResourceDictionary do not resolve a DataContext
        // naturally — set the CollectionViewSource's Source explicitly so
        // the navigation tree groups by Section against the VM's Items.
        ((CollectionViewSource)Resources["NavView"]).Source = viewModel.Items;

        viewModel.NavigationCancelled += OnNavigationCancelled;
        Loaded += OnLoadedAsync;
    }

    /// <summary>
    /// One-shot initial-selection driver. Navigates to the first catalog
    /// entry (Pulse Dashboard) so the shell lands on a known section
    /// when the window comes up.
    /// </summary>
    // async void is the WPF idiom for Loaded handlers (the event signature
    // is sync RoutedEventHandler). Any exception thrown here propagates to
    // the dispatcher's unhandled-exception path; Chunk E5 will install a
    // global handler that surfaces those via the same correlation-id
    // pattern LoginViewModel uses.
    private async void OnLoadedAsync(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoadedAsync;
        await _viewModel.NavigateToCommand.ExecuteAsync(NavigationCatalog.AllItems[0]);
        // Prime the Pulse drawer's tile snapshot so the topbar's
        // RedCount / AmberCount bindings populate before the user
        // opens the drawer. Subsequent refreshes are explicit (the
        // refresh button in the drawer header).
        await _viewModel.PulseDrawerViewModel.RefreshTilesCommand.ExecuteAsync(null);
    }

    /// <summary>
    /// Hook for <see cref="MainShellViewModel.NavigationCancelled"/>.
    /// </summary>
    /// <remarks>
    /// Today this is a no-op. The navigation tree's active-row treatment
    /// is bound (via <c>ReferenceEqualityMultiConverter</c>) to the VM's
    /// <c>SelectedItem</c>, not to an independent visual selection — so
    /// when the dirty-state guard rejects a nav and the VM leaves
    /// <c>SelectedItem</c> untouched, the visual selection has nothing
    /// to roll back. The subscription stays in place so a future
    /// rendering change (e.g., a real TreeView with two-way
    /// <c>SelectedItem</c> binding that needs roll-back) has a hook
    /// already wired without a behavioral change here.
    /// </remarks>
    private void OnNavigationCancelled(object? sender, NavigationCancelledEventArgs e)
    {
        // Intentional no-op — see method remarks.
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        _viewModel.NavigationCancelled -= OnNavigationCancelled;
        Loaded -= OnLoadedAsync;
        base.OnClosed(e);
    }
}
