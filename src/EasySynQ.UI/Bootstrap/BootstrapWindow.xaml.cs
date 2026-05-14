using System.Windows;

namespace EasySynQ.UI.Bootstrap;

/// <summary>
/// View for the first-run bootstrap flow. Code-behind is
/// intentionally minimal — all logic lives in
/// <see cref="BootstrapViewModel"/>. This file is restricted to:
/// <list type="number">
///   <item>Pushing both <c>PasswordBox</c> controls' current text to
///   the view model via
///   <see cref="BootstrapViewModel.SetPasswords(string, string)"/>
///   on each <c>PasswordChanged</c> event (the only way around
///   <c>PasswordBox</c>'s non-bindable design).</item>
///   <item>Setting initial keyboard focus to the username field on
///   load and unsubscribing on close.</item>
/// </list>
/// The <see cref="BootstrapViewModel.BootstrapSucceeded"/> and
/// <see cref="BootstrapViewModel.IdempotencyGuardFired"/> events are
/// NOT subscribed here — <see cref="App"/> owns both post-bootstrap
/// transitions (success: populate accessor, resolve MainWindow,
/// close this window; idempotency: dialog + shutdown). Mirrors
/// <c>LoginWindow</c>'s success-transition ownership for the same
/// zero-window-gap and centralized-lifecycle reasons.
/// </summary>
public partial class BootstrapWindow : Window
{
    /// <summary>
    /// View model the window binds against. Exposed publicly so the
    /// host (<see cref="App"/>) can subscribe to
    /// <see cref="BootstrapViewModel.BootstrapSucceeded"/> and
    /// <see cref="BootstrapViewModel.IdempotencyGuardFired"/>
    /// without going through the loosely-typed
    /// <see cref="FrameworkElement.DataContext"/>.
    /// </summary>
    public BootstrapViewModel ViewModel { get; }

    /// <summary>
    /// Constructs the window over its view model. The view model is
    /// also assigned to <see cref="FrameworkElement.DataContext"/>.
    /// </summary>
    /// <param name="viewModel">View model. Must not be
    /// <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when
    /// <paramref name="viewModel"/> is
    /// <see langword="null"/>.</exception>
    public BootstrapWindow(BootstrapViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UsernameTextBox.Focus();
    }

    /// <summary>
    /// Shared <c>PasswordChanged</c> handler for both
    /// <c>PasswordBox</c> controls. Reads the current text from both
    /// boxes and pushes the pair to the view model via
    /// <see cref="BootstrapViewModel.SetPasswords(string, string)"/>.
    /// <paramref name="sender"/> is unused — either box's change is
    /// sufficient reason to re-sync both values.
    /// </summary>
    private void PasswordChanged(object sender, RoutedEventArgs e)
    {
        ViewModel.SetPasswords(PasswordBox.Password, ConfirmPasswordBox.Password);
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        Loaded -= OnLoaded;
        base.OnClosed(e);
    }
}
