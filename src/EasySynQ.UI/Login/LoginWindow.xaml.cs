using System.Windows;
using System.Windows.Data;
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
///   <item>Translating the view model's <see cref="LoginViewModel.LoginSucceeded"/>
///   and <see cref="LoginViewModel.BootstrapRequired"/> events into a
///   window-close result.</item>
///   <item>Setting initial keyboard focus to the username field on load
///   and unsubscribing on close.</item>
/// </list>
/// </summary>
public partial class LoginWindow : Window
{
    private readonly LoginViewModel _viewModel;

    /// <summary>
    /// Constructs the window over its view model. The view model is also
    /// the <see cref="FrameworkElement.DataContext"/>.
    /// </summary>
    /// <param name="viewModel">View model. Must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when
    /// <paramref name="viewModel"/> is <see langword="null"/>.</exception>
    public LoginWindow(LoginViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        _viewModel.LoginSucceeded += OnLoginSucceeded;
        _viewModel.BootstrapRequired += OnBootstrapRequired;
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

    private void OnLoginSucceeded(object? sender, AuthenticatedUserEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnBootstrapRequired(object? sender, EventArgs e)
    {
        // TODO Phase 1 follow-up: route to a real first-run bootstrap
        // window once that flow is designed. Placeholder dialog here
        // exists only so a fresh install does not silently dead-end.
        MessageBox.Show(
            this,
            "First-run setup is not yet implemented in this build. Contact your administrator.",
            "Setup Required",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        DialogResult = false;
        Close();
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        _viewModel.LoginSucceeded -= OnLoginSucceeded;
        _viewModel.BootstrapRequired -= OnBootstrapRequired;
        Loaded -= OnLoaded;
        base.OnClosed(e);
    }
}
