using System.Windows;
using System.Windows.Input;

using EasySynQ.Services.Abstractions;
using EasySynQ.Services.LockReasons;

namespace EasySynQ.UI.LockInspector;

/// <summary>
/// Production <see cref="ILockInspectorPrompter"/>. Constructs a
/// fresh <see cref="LockInspectorViewModel"/> per call, presents the
/// <see cref="LockInspectorPopover"/> window non-modally near the
/// current mouse position, and lets the window's
/// <see cref="Window.Deactivated"/> handler close it on click-outside
/// (ADR 0012 C7b).
/// </summary>
/// <remarks>
/// <para>
/// <b>Lifecycle.</b> Singleton in DI. Each call constructs a fresh
/// Window — WPF Windows are single-show. Position is computed from
/// the cursor's screen coordinates at the moment <see cref="OpenAsync"/>
/// runs; the popover appears near where the user clicked.
/// </para>
/// <para>
/// <b>Non-modal.</b> The prompter calls <see cref="Window.Show"/>
/// (not <see cref="Window.ShowDialog"/>) so the user can continue
/// interacting with the parent surface while the popover is open;
/// the popover auto-closes when focus leaves it (matching the
/// "informational glance" UX promise of ADR 0012).
/// </para>
/// </remarks>
public sealed class LockInspectorPrompter : ILockInspectorPrompter
{
    private readonly ILockReasonRepository _lockReasons;
    private readonly ILockReasonResolverRegistry _registry;

    /// <summary>Constructs the prompter over its scoped service
    /// dependencies.</summary>
    public LockInspectorPrompter(
        ILockReasonRepository lockReasons,
        ILockReasonResolverRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(lockReasons);
        ArgumentNullException.ThrowIfNull(registry);

        _lockReasons = lockReasons;
        _registry = registry;
    }

    /// <inheritdoc />
    public async Task OpenAsync(
        string lockedEntityType,
        string lockedEntityId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockedEntityType);
        ArgumentException.ThrowIfNullOrWhiteSpace(lockedEntityId);

        var vm = new LockInspectorViewModel(
            lockedEntityType,
            lockedEntityId,
            _lockReasons,
            _registry);

        // Load the chain BEFORE showing the window so the popover
        // appears already populated; an empty-then-filling popover
        // would flicker the chain in after presentation. The VM's
        // IsLoading flag stays available for the unlikely-but-possible
        // case where someone keeps a reference to the VM and triggers
        // a future re-load.
        await vm.LoadAsync(cancellationToken);

        var window = new LockInspectorPopover
        {
            DataContext = vm,
        };

        // Position near the cursor. Mouse.GetPosition(null) on its own
        // returns a point relative to the focused element; combine
        // with PointToScreen against a top-level Window to get screen
        // coordinates. Falls back to the centred-on-MainWindow
        // position when no anchor is available.
        if (Application.Current?.MainWindow is { } mainWindow)
        {
            var mousePos = mainWindow.PointToScreen(Mouse.GetPosition(mainWindow));
            // Offset slightly so the popover does not appear under the
            // cursor (the user's pointer is still hovering the click
            // target).
            window.Left = mousePos.X + 8;
            window.Top = mousePos.Y + 8;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Owner = mainWindow;
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        window.Show();
    }
}
