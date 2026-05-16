using System.Windows;
using System.Windows.Controls;

namespace EasySynQ.UI.Documents.Comments;

/// <summary>
/// User control hosting the C6b reviewer-comment panel (ADR 0008
/// C6b stop 6). Thin code-behind: triggers the VM's
/// <c>LoadCommand</c> on <see cref="FrameworkElement.Loaded"/> so
/// the panel populates its thread when the detail view embeds it.
/// </summary>
public partial class CommentPanelControl : UserControl
{
    /// <summary>Constructs the control. The view model is supplied
    /// via <c>DataContext</c> (typically by the detail view that
    /// embeds the panel).</summary>
    public CommentPanelControl()
    {
        InitializeComponent();
    }

    private async void OnLoadedAsync(object sender, RoutedEventArgs e)
    {
        if (DataContext is not CommentPanelViewModel vm)
        {
            return;
        }

        await vm.LoadCommand.ExecuteAsync(null);
    }
}
