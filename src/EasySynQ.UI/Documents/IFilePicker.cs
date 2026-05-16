namespace EasySynQ.UI.Documents;

/// <summary>
/// Abstraction over <see cref="Microsoft.Win32.OpenFileDialog"/> so
/// "pick a file" interactions can be mocked in view-model unit tests
/// without touching WPF. The production implementation
/// (<c>WpfFilePicker</c>) wraps the WPF dialog; tests substitute a
/// stub that returns a known path or null.
/// </summary>
/// <remarks>
/// The interface intentionally exposes only what the C6a document
/// surfaces need: a "pick one file" gesture filtered to a specific
/// extension, returning the selected absolute path or
/// <see langword="null"/> when the user cancels. Multi-select, save
/// dialog, folder picker — all out of scope until a consumer needs
/// them.
/// </remarks>
public interface IFilePicker
{
    /// <summary>
    /// Shows a system file-open dialog filtered to
    /// <paramref name="filter"/> (in standard
    /// <see cref="Microsoft.Win32.FileDialog.Filter"/> format, e.g.
    /// <c>"PDF documents (*.pdf)|*.pdf"</c>) with the supplied
    /// <paramref name="title"/>. Returns the selected absolute file
    /// path, or <see langword="null"/> when the user cancels.
    /// </summary>
    /// <param name="title">Dialog window title.</param>
    /// <param name="filter">File-extension filter string.</param>
    /// <returns>Selected absolute path, or <see langword="null"/>
    /// on cancel.</returns>
    string? PickFile(string title, string filter);
}
