using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace EasySynQ.UI.Documents.EditMetadata;

/// <summary>
/// View model for the Edit Metadata modal dialog (ADR 0008 C6a).
/// Captures updated Number and Title for a Document whose latest
/// revision is in Draft. OK is enabled when both Number and Title
/// are non-empty; Cancel is always enabled.
/// </summary>
public sealed partial class EditMetadataViewModel : ObservableObject
{
    private readonly Action<bool> _closeDialog;

    /// <summary>
    /// Constructs the view model pre-populated with the current
    /// values and a close-dialog callback. The callback receives
    /// <see langword="true"/> for OK and <see langword="false"/> for
    /// Cancel.
    /// </summary>
    /// <param name="currentNumber">Current document number — used to
    /// seed <see cref="Number"/>. Must not be null/empty.</param>
    /// <param name="currentTitle">Current document title — used to
    /// seed <see cref="Title"/>. Must not be null/empty.</param>
    /// <param name="closeDialog">Callback invoked with the dialog
    /// result on OK or Cancel. Must not be <see langword="null"/>.</param>
    public EditMetadataViewModel(
        string currentNumber,
        string currentTitle,
        Action<bool> closeDialog)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentTitle);
        ArgumentNullException.ThrowIfNull(closeDialog);

        Number = currentNumber;
        Title = currentTitle;
        _closeDialog = closeDialog;
    }

    /// <summary>The org-assigned document number, pre-populated with
    /// the current value.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OkCommand))]
    public partial string Number { get; set; }

    /// <summary>The document title, pre-populated with the current
    /// value.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OkCommand))]
    public partial string Title { get; set; }

    /// <summary>
    /// OK command — enabled only when Number and Title are both
    /// non-empty. Closes the dialog with a positive result.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanOk))]
    private void Ok() => _closeDialog(true);

    /// <summary>
    /// Cancel command — always enabled. Closes the dialog with a
    /// negative result.
    /// </summary>
    [RelayCommand]
    private void Cancel() => _closeDialog(false);

    private bool CanOk() =>
        !string.IsNullOrWhiteSpace(Number) &&
        !string.IsNullOrWhiteSpace(Title);
}
