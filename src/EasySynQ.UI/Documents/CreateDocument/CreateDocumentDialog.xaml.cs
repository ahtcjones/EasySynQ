using System.Windows;

namespace EasySynQ.UI.Documents.CreateDocument;

/// <summary>
/// Modal dialog for the C6a Create Document affordance. Purely a
/// view — all logic lives in <see cref="CreateDocumentViewModel"/>;
/// the code-behind exists only to translate the view model's close
/// callback into <see cref="Window.DialogResult"/> (the standard WPF
/// mechanism for <see cref="Window.ShowDialog"/> to return).
/// </summary>
public partial class CreateDocumentDialog : Window
{
    /// <summary>The view model the window binds against.</summary>
    public CreateDocumentViewModel ViewModel { get; }

    /// <summary>
    /// Constructs the dialog over a fresh view model with the supplied
    /// file picker. The view-model's close callback is wired to set
    /// this Window's <see cref="Window.DialogResult"/> and trigger
    /// <see cref="Window.Close"/>.
    /// </summary>
    /// <param name="filePicker">Abstraction over the WPF file-open
    /// dialog, used by the view model's PickPdf command. Must not be
    /// <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when
    /// <paramref name="filePicker"/> is <see langword="null"/>.</exception>
    public CreateDocumentDialog(IFilePicker filePicker)
    {
        ArgumentNullException.ThrowIfNull(filePicker);
        ViewModel = new CreateDocumentViewModel(filePicker, CloseDialog);
        DataContext = ViewModel;
        InitializeComponent();
    }

    private void CloseDialog(bool ok)
    {
        // Setting DialogResult on a modal Window automatically calls
        // Close() — the prompter then reads DialogResult and reads the
        // ViewModel's captured values to decide what to return.
        DialogResult = ok;
    }
}
