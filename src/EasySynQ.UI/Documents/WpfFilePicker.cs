using Microsoft.Win32;

namespace EasySynQ.UI.Documents;

/// <summary>
/// Production <see cref="IFilePicker"/> wrapping
/// <see cref="OpenFileDialog"/>. Stateless; safe as a singleton.
/// </summary>
public sealed class WpfFilePicker : IFilePicker
{
    /// <inheritdoc />
    public string? PickFile(string title, string filter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(filter);

        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = filter,
            CheckFileExists = true,
            Multiselect = false,
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
