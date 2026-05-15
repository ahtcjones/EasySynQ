using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace EasySynQ.UI.Signing;

/// <summary>
/// View model for the <see cref="SignAsRoleDialog"/> (ADR 0009 C4).
/// Presents an eligible-role list, captures the user's pick, and
/// exposes OK / Cancel commands that the dialog binds to.
/// </summary>
/// <remarks>
/// <para>
/// <b>Dumb view-model.</b> Filtering of the user's roles by the gating
/// permission happens in <see cref="SignatureRolePrompter"/> via
/// <see cref="EasySynQ.Services.Authorization.IRoleResolutionService"/>.
/// This view model takes the already-filtered list verbatim and only
/// owns the user-selection mechanics. No DI; the prompter constructs
/// the view model directly with the filtered roles.
/// </para>
/// <para>
/// <b>Result observation.</b> Confirmation that the user clicked OK
/// (rather than Cancel) is signalled by <see cref="DialogResult"/> on
/// the owning Window — set by the OK / Cancel commands and read by the
/// prompter after <c>ShowDialog()</c> returns. The
/// <see cref="SelectedRole"/> property carries the picked value when
/// confirmed.
/// </para>
/// </remarks>
public sealed partial class SignAsRoleViewModel : ObservableObject
{
    private readonly Action<bool> _closeDialog;

    /// <summary>
    /// The eligible roles passed in by the prompter, in stable order
    /// (the resolution service sorts ordinal so dialog rendering is
    /// deterministic across sessions).
    /// </summary>
    public IReadOnlyList<string> Roles { get; }

    /// <summary>The role currently selected in the picker, or
    /// <see langword="null"/> if no selection has been made yet.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OkCommand))]
    public partial string? SelectedRole { get; set; }

    /// <summary>
    /// Constructs the view model with the supplied eligible roles and a
    /// callback the OK / Cancel commands invoke to close the owning
    /// Window. The callback receives <see langword="true"/> for OK
    /// (a role was picked) and <see langword="false"/> for Cancel.
    /// </summary>
    /// <param name="roles">Eligible roles; must not be
    /// <see langword="null"/> and must not be empty (the prompter
    /// guarantees this — empty is a defensive throw, not a dialog
    /// state).</param>
    /// <param name="closeDialog">Callback invoked with the dialog
    /// result on OK or Cancel. Must not be <see langword="null"/>.</param>
    public SignAsRoleViewModel(IReadOnlyList<string> roles, Action<bool> closeDialog)
    {
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentNullException.ThrowIfNull(closeDialog);
        if (roles.Count == 0)
        {
            throw new ArgumentException(
                "Roles must not be empty — the prompter is responsible for the empty-eligible-roles defensive throw before reaching this view model.",
                nameof(roles));
        }

        Roles = roles;
        _closeDialog = closeDialog;
    }

    /// <summary>
    /// OK command — enabled only when a role is selected. Closes the
    /// dialog with a positive result so the prompter returns
    /// <see cref="SelectedRole"/>.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanOk))]
    private void Ok()
    {
        _closeDialog(true);
    }

    /// <summary>
    /// Cancel command — always enabled. Closes the dialog with a
    /// negative result so the prompter raises
    /// <see cref="OperationCanceledException"/>.
    /// </summary>
    [RelayCommand]
    private void Cancel()
    {
        _closeDialog(false);
    }

    private bool CanOk() => !string.IsNullOrEmpty(SelectedRole);
}
