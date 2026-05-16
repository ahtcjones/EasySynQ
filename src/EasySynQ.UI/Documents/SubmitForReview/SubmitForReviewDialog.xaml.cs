using System.Windows;

using EasySynQ.Services.Abstractions;
using EasySynQ.Services.Documents;
using EasySynQ.Services.Time;
using EasySynQ.UI.Signing;

namespace EasySynQ.UI.Documents.SubmitForReview;

/// <summary>
/// Modal "submit for review" dialog (ADR 0008 C6b). The code-behind
/// is intentionally thin: it constructs the heavy
/// <see cref="SubmitForReviewViewModel"/>, wires the close callback
/// to the Window's <c>DialogResult</c>, and triggers the
/// reviewer-candidate load on <see cref="FrameworkElement.Loaded"/>
/// (so the dialog can render its loading indicator before the data
/// arrives).
/// </summary>
public partial class SubmitForReviewDialog : Window
{
    /// <summary>The view model the window binds against.</summary>
    public SubmitForReviewViewModel ViewModel { get; }

    /// <summary>
    /// Constructs the dialog over a fresh view model. The supplied
    /// dependencies are forwarded to
    /// <see cref="SubmitForReviewViewModel"/> verbatim; the
    /// close-callback is wired to <see cref="Window.DialogResult"/>.
    /// </summary>
    /// <param name="revisionId">Draft revision being submitted.</param>
    /// <param name="users">User-repository surface.</param>
    /// <param name="rolePrompter">Role-prompter for the signing
    /// path.</param>
    /// <param name="lifecycle">Lifecycle service for the actual
    /// submit transaction.</param>
    /// <param name="clock">Clock for the <c>asOfUtc</c> at which
    /// to resolve effective permissions.</param>
    public SubmitForReviewDialog(
        Guid revisionId,
        IUserRepository users,
        ISignatureRolePrompter rolePrompter,
        IDocumentLifecycleService lifecycle,
        IClock clock)
    {
        ViewModel = new SubmitForReviewViewModel(
            revisionId, users, rolePrompter, lifecycle, clock, CloseDialog);
        DataContext = ViewModel;
        InitializeComponent();
    }

    private async void OnLoadedAsync(object sender, RoutedEventArgs e)
    {
        // Fire-and-forget the candidate load. Errors land on the
        // VM's ErrorMessage (the dialog stays open and the user
        // sees the failure); successful loads populate Picker and
        // the user can begin selecting.
        await ViewModel.LoadCandidatesCommand.ExecuteAsync(null);
    }

    private void CloseDialog(bool ok)
    {
        // Setting DialogResult on a modal Window calls Close()
        // automatically — the prompter then reads DialogResult to
        // decide its return value.
        DialogResult = ok;
    }
}
