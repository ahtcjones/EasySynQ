using System.Windows.Controls;

namespace EasySynQ.UI.Placeholders;

/// <summary>
/// Code-behind for <see cref="PulseDashboardView"/>. Empty by
/// design — the view binds to <see cref="PulseDashboardViewModel"/>
/// via DataContext (no instance state today) and renders the
/// placeholder content with no per-view logic.
/// </summary>
public partial class PulseDashboardView : UserControl
{
    /// <summary>Parameterless constructor for XAML instantiation.</summary>
    public PulseDashboardView()
    {
        InitializeComponent();
    }
}
