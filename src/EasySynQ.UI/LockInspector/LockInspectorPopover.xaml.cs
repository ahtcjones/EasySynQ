using System.Windows;

namespace EasySynQ.UI.LockInspector;

/// <summary>
/// Code-behind for <see cref="LockInspectorPopover"/> (ADR 0012 C7b).
/// Hosts the lock-inspector view and closes itself when focus moves
/// away (the popover's "click outside dismisses" UX promise).
/// </summary>
public partial class LockInspectorPopover : Window
{
    /// <summary>Parameterless constructor for XAML instantiation.</summary>
    public LockInspectorPopover()
    {
        InitializeComponent();
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        // Auto-close on click outside. WPF fires Deactivated when the
        // window loses focus to any other window (including the
        // parent). This matches the WPF Popup primitive's
        // StaysOpen=False behavior but is implemented here with a
        // chromeless Window because the codebase's existing prompter
        // pattern is Window-based (per ADR 0012's prompter framing).
        Close();
    }
}
