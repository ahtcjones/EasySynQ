using System.Windows;

using EasySynQ.Services.Abstractions;
using EasySynQ.Services.Documents;
using EasySynQ.UI.Signing;

namespace EasySynQ.UI.Documents.ReviewAndSign;

/// <summary>
/// Modal "review and sign" dialog (ADR 0008 C6b stop 4). The
/// code-behind constructs the <see cref="ReviewAndSignViewModel"/>,
/// wires the close callback to the Window's <c>DialogResult</c>,
/// and triggers the role-resolution on <see cref="FrameworkElement.Loaded"/>
/// so the dialog can render its "Resolving signing role…" indicator
/// while the role-picker dialog (multi-role users) is up.
/// </summary>
public partial class ReviewAndSignDialog : Window
{
    /// <summary>The view model the window binds against.</summary>
    public ReviewAndSignViewModel ViewModel { get; }

    /// <summary>
    /// Constructs the dialog over a fresh view model. The supplied
    /// dependencies are forwarded verbatim; the close-callback is
    /// wired to <see cref="Window.DialogResult"/>.
    /// </summary>
    /// <param name="revisionId">InReview revision being signed.</param>
    /// <param name="documentTitle">Parent Document's title for the
    /// confirmation message.</param>
    /// <param name="revisionLabel">Revision label for the
    /// confirmation message.</param>
    /// <param name="rolePrompter">Role-prompter for the
    /// multi-role signing path.</param>
    /// <param name="lifecycle">Lifecycle service for the sign
    /// transaction.</param>
    /// <param name="revisions">Revision repository for the
    /// post-sign state lookup.</param>
    public ReviewAndSignDialog(
        Guid revisionId,
        string documentTitle,
        string revisionLabel,
        ISignatureRolePrompter rolePrompter,
        IDocumentLifecycleService lifecycle,
        IDocumentRevisionRepository revisions)
    {
        ViewModel = new ReviewAndSignViewModel(
            revisionId,
            documentTitle,
            revisionLabel,
            rolePrompter,
            lifecycle,
            revisions,
            CloseDialog);
        DataContext = ViewModel;
        InitializeComponent();
    }

    private async void OnLoadedAsync(object sender, RoutedEventArgs e)
    {
        // Resolve the signing role. Single-role users auto-resolve
        // (no picker); multi-role users see the role picker on
        // top of this dialog. On cancel, the VM closes this dialog
        // automatically.
        await ViewModel.ResolveRoleCommand.ExecuteAsync(null);
    }

    private void CloseDialog(bool ok)
    {
        // Setting DialogResult on a modal Window calls Close()
        // automatically.
        DialogResult = ok;
    }
}
