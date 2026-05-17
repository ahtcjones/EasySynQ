using System.Windows.Controls;

namespace EasySynQ.UI.LockInspector;

/// <summary>
/// Code-behind for <see cref="LockInspectorPopover"/> (ADR 0012 C7b).
/// Hosts the lock-inspector chain rendering inside a WPF
/// <see cref="System.Windows.Controls.Primitives.Popup"/> primitive
/// (configured by <see cref="LockInspectorPrompter"/>). The control
/// has no behavioural code — positioning and click-outside dismissal
/// are the Popup's responsibility per ADR 0012's chosen UX shape.
/// </summary>
public partial class LockInspectorPopover : UserControl
{
    /// <summary>Parameterless constructor for XAML instantiation.</summary>
    public LockInspectorPopover()
    {
        InitializeComponent();
    }
}
