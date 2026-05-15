using System.Windows;

namespace EasySynQ.UI.Signing;

/// <summary>
/// Modal dialog presented to multi-role users at signing time
/// (ADR 0009 C4). Purely a view — all logic lives in
/// <see cref="SignAsRoleViewModel"/>; the code-behind exists only to
/// translate the view model's close-callback into <c>DialogResult</c>
/// (the standard WPF mechanism for <c>ShowDialog()</c> to return).
/// </summary>
public partial class SignAsRoleDialog : Window
{
    /// <summary>The view model the window binds against.</summary>
    public SignAsRoleViewModel ViewModel { get; }

    /// <summary>
    /// Constructs the dialog over a fresh view model with the supplied
    /// eligible roles. The view-model's close callback is wired to set
    /// this Window's <see cref="Window.DialogResult"/> and trigger
    /// <see cref="Window.Close"/>.
    /// </summary>
    /// <param name="roles">Eligible roles. Must not be
    /// <see langword="null"/>; must not be empty (the prompter
    /// guarantees both).</param>
    /// <exception cref="ArgumentNullException">Thrown when
    /// <paramref name="roles"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when
    /// <paramref name="roles"/> is empty.</exception>
    public SignAsRoleDialog(IReadOnlyList<string> roles)
    {
        ArgumentNullException.ThrowIfNull(roles);
        ViewModel = new SignAsRoleViewModel(roles, CloseDialog);
        DataContext = ViewModel;
        InitializeComponent();
    }

    private void CloseDialog(bool ok)
    {
        // Setting DialogResult on a modal Window automatically calls
        // Close() — the prompter then reads DialogResult and the
        // ViewModel's SelectedRole to decide what to return.
        DialogResult = ok;
    }
}
