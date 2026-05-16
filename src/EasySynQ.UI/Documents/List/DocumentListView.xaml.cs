using System.Windows;
using System.Windows.Controls;

namespace EasySynQ.UI.Documents.List;

/// <summary>
/// Code-behind for <see cref="DocumentListView"/>. Triggers
/// <see cref="DocumentListViewModel.LoadCommand"/> when the view is
/// first attached so the DataGrid populates without requiring an
/// explicit "Load" button. All other behavior lives in the
/// view model.
/// </summary>
public partial class DocumentListView : UserControl
{
    /// <summary>Parameterless constructor for XAML instantiation.</summary>
    public DocumentListView()
    {
        InitializeComponent();
    }

    private async void OnLoadedAsync(object sender, RoutedEventArgs e)
    {
        if (DataContext is DocumentListViewModel vm
            && vm.LoadCommand.CanExecute(null))
        {
            // CommunityToolkit.Mvvm's async RelayCommand exposes
            // ExecuteAsync; the void Loaded handler awaits it so any
            // exception surfaces to the dispatcher rather than being
            // silently dropped.
            await vm.LoadCommand.ExecuteAsync(null);
        }
    }
}
