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
    /// <summary>Parameterless constructor for XAML instantiation.</summary>
    public DocumentDetailView()
    {
        InitializeComponent();
    }

    private async void OnLoadedAsync(object sender, RoutedEventArgs e)
    {
        if (DataContext is DocumentDetailViewModel vm
            && vm.LoadCommand.CanExecute(null))
        {
            await vm.LoadCommand.ExecuteAsync(null);
        }
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
