using System.Windows;
using System.Windows.Controls;

namespace EasySynQ.UI.Login;

/// <summary>
/// View for the login flow. Code-behind is intentionally minimal — all
/// authentication logic lives in <see cref="LoginViewModel"/>. This file
/// is restricted to:
/// <list type="number">
///   <item>Wiring <see cref="PasswordBox.Password"/> through to the
///   command parameter on each keystroke (the only way around
///   <c>PasswordBox</c>'s non-bindable design).</item>
///   <item>Setting initial keyboard focus to the username field on load
///   and unsubscribing on close.</item>
/// </list>
/// The <see cref="LoginViewModel.LoginSucceeded"/> event is NOT
/// subscribed here — <see cref="App"/> owns the post-success transition
/// (populate the current-user accessor, resolve and show MainWindow,
/// then close this window). Two reasons: the close ordering matters
/// (MainWindow must open before this Window closes, otherwise default
/// <c>ShutdownMode.OnLastWindowClose</c> triggers app shutdown during
/// the zero-window gap), and centralizing the success-flow ownership
/// in the host makes the lifecycle easier to reason about than
/// splitting it across two files.
/// </summary>
public partial class LoginWindow : Window
{
    /// <summary>
    /// View model the window binds against. Exposed publicly so the
    /// host (<see cref="App"/>) can subscribe to
    /// <see cref="LoginViewModel.LoginSucceeded"/> without going
    /// through the loosely-typed <see cref="FrameworkElement.DataContext"/>.
    /// </summary>
    public LoginViewModel ViewModel { get; }

    /// <summary>
    /// Constructs the window over its view model. The view model is
    /// also assigned to <see cref="FrameworkElement.DataContext"/>.
    /// </summary>
    /// <param name="viewModel">View model. Must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when
    /// <paramref name="viewModel"/> is <see langword="null"/>.</exception>
    public LoginWindow(LoginViewModel viewModel)
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

    private void PasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        // PasswordBox.Password raises no INotifyPropertyChanged event;
        // the CommandParameter binding on SignInButton would otherwise
        // capture only the initial empty value. Forcing UpdateTarget
        // re-reads Password and pushes the new value through.
        SignInButton
            .GetBindingExpression(Button.CommandParameterProperty)?
            .UpdateTarget();
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        Loaded -= OnLoaded;
        base.OnClosed(e);
    }
}
