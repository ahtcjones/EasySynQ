using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace EasySynQ.UI.Documents.CreateDocument;

/// <summary>
/// View model for the Create Document modal dialog (ADR 0008 C6a).
/// Captures Number, Title, and an optional initial PDF path. OK is
/// enabled when both Number and Title are non-empty; Cancel is
/// always enabled. The OK / Cancel commands close the owning Window
/// via the supplied callback (matching the
/// <see cref="EasySynQ.UI.Signing.SignAsRoleViewModel"/> pattern).
/// </summary>
/// <remarks>
/// <para>
/// <b>File picking is delegated.</b> The view model takes an
/// <see cref="IFilePicker"/> dependency rather than constructing a
/// WPF dialog directly, keeping the VM headlessly unit-testable.
/// </para>
/// <para>
/// <b>Library is internal-only.</b> The C6a brief locks the
/// new-document library to Internal. The data model has no
/// <c>Library</c> discriminator on <see cref="EasySynQ.Domain.Entities.Documents.Document"/>
/// anyway (External specs live in a separate <c>ExternalDocument</c>
/// entity); the absence of a library selector here reflects that
/// schema reality.
/// </para>
/// </remarks>
public sealed partial class CreateDocumentViewModel : ObservableObject
{
    private const string PdfFilterTitle = "Select PDF document";
    private const string PdfFilter = "PDF documents (*.pdf)|*.pdf";

    private readonly Action<bool> _closeDialog;
    private readonly IFilePicker _filePicker;

    /// <summary>
    /// Constructs the view model with a file-picker dependency and a
    /// callback the OK / Cancel commands invoke to close the owning
    /// Window. The callback receives <see langword="true"/> for OK
    /// (both Number and Title were supplied) and <see langword="false"/>
    /// for Cancel.
    /// </summary>
    /// <param name="filePicker">Abstraction over the WPF file-open
    /// dialog. Must not be <see langword="null"/>.</param>
    /// <param name="closeDialog">Callback invoked with the dialog
    /// result on OK or Cancel. Must not be <see langword="null"/>.</param>
    public CreateDocumentViewModel(IFilePicker filePicker, Action<bool> closeDialog)
    {
        ArgumentNullException.ThrowIfNull(filePicker);
        ArgumentNullException.ThrowIfNull(closeDialog);

        _filePicker = filePicker;
        _closeDialog = closeDialog;
    }

    /// <summary>The org-assigned document number. Bound to the
    /// dialog's Number text box.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OkCommand))]
    public partial string Number { get; set; } = string.Empty;

    /// <summary>The document title. Bound to the dialog's Title text
    /// box.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OkCommand))]
    public partial string Title { get; set; } = string.Empty;

    /// <summary>
    /// Absolute path of the optional initial PDF the user picked, or
    /// <see langword="null"/> when no PDF has been picked. Bound to
    /// the dialog's "Selected file" display.
    /// </summary>
    [ObservableProperty]
    public partial string? SelectedPdfPath { get; set; }

    /// <summary>
    /// File-picker command — invokes the injected
    /// <see cref="IFilePicker"/> and updates
    /// <see cref="SelectedPdfPath"/> if the user picked a file.
    /// Always enabled (the user may re-pick to change their mind).
    /// </summary>
    [RelayCommand]
    private void PickPdf()
    {
        var picked = _filePicker.PickFile(PdfFilterTitle, PdfFilter);
        if (picked is not null)
        {
            SelectedPdfPath = picked;
        }
    }

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
