using System.Windows;

using EasySynQ.Services.Documents;

namespace EasySynQ.UI.Documents.ReturnToDraft;

/// <summary>
/// Modal "return to draft" dialog (ADR 0008 C6b stop 5). Thin
/// code-behind: constructs the
/// <see cref="ReturnToDraftViewModel"/>, wires the close callback
/// to <see cref="Window.DialogResult"/>.
/// </summary>
public partial class ReturnToDraftDialog : Window
{
    /// <summary>The view model the window binds against.</summary>
    public ReturnToDraftViewModel ViewModel { get; }

    /// <summary>
    /// Constructs the dialog over a fresh view model. The
    /// close-callback is wired to <see cref="Window.DialogResult"/>.
    /// </summary>
    /// <param name="revisionId">InReview revision being returned
    /// to Draft.</param>
    /// <param name="lifecycle">Lifecycle service for the return
    /// transaction.</param>
    public ReturnToDraftDialog(Guid revisionId, IDocumentLifecycleService lifecycle)
    {
        ViewModel = new ReturnToDraftViewModel(revisionId, lifecycle, CloseDialog);
        DataContext = ViewModel;
        InitializeComponent();
    }

    private void CloseDialog(bool ok)
    {
        // Setting DialogResult on a modal Window calls Close()
        // automatically.
        DialogResult = ok;
    }
}
