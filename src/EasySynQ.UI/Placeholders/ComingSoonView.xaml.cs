using System.Windows.Controls;

namespace EasySynQ.UI.Placeholders;

/// <summary>
/// Code-behind for <see cref="ComingSoonView"/>. Empty by design —
/// the view binds to <see cref="ComingSoonViewModel"/> via DataContext
/// and renders the placeholder content with no per-view logic.
/// </summary>
public partial class ComingSoonView : UserControl
{
    /// <summary>Parameterless constructor for XAML instantiation.</summary>
    public ComingSoonView()
    {
        InitializeComponent();
    }
}
