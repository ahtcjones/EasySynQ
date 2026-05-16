using System.Windows;

namespace EasySynQ.UI.Documents.EditMetadata;

/// <summary>
/// Modal dialog for the C6a Edit Metadata affordance. Purely a view —
/// all logic lives in <see cref="EditMetadataViewModel"/>; the
/// code-behind exists only to translate the view model's close callback
/// into <see cref="Window.DialogResult"/>.
/// </summary>
public partial class EditMetadataDialog : Window
{
    /// <summary>The view model the window binds against.</summary>
    public EditMetadataViewModel ViewModel { get; }

    /// <summary>
    /// Constructs the dialog with a fresh view model pre-populated
    /// from the supplied current values.
    /// </summary>
    /// <param name="currentNumber">Current document number. Must not
    /// be null/empty.</param>
    /// <param name="currentTitle">Current document title. Must not
    /// be null/empty.</param>
    /// <exception cref="ArgumentException">Thrown when either input
    /// is null/empty/whitespace.</exception>
    public EditMetadataDialog(string currentNumber, string currentTitle)
    {
        ViewModel = new EditMetadataViewModel(currentNumber, currentTitle, CloseDialog);
        DataContext = ViewModel;
        InitializeComponent();
    }

    private void CloseDialog(bool ok)
    {
        DialogResult = ok;
    }
}
