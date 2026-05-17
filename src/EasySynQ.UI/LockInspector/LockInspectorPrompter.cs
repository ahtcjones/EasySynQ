using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

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

        // Position near the cursor and parent to a real Window if we
        // can find one that's still alive. Application.Current.MainWindow
        // can be a closed or never-shown Window after the sign-in flow
        // (the LoginWindow's close auto-clears MainWindow, and the
        // shell's Show does not re-assign it — so the property may
        // point at a defunct reference). PresentationSource.FromVisual
        // returning null is the reliable "this Window has no live
        // Hwnd" check; both PointToScreen and Owner-assignment throw
        // on such windows. Fall back to center-screen when no live
        // owner is available.
        var owner = FindOwnerWindow();
        if (owner is not null)
        {
            var mousePos = owner.PointToScreen(Mouse.GetPosition(owner));
            // Offset slightly so the popover does not appear under the
            // cursor (the user's pointer is still hovering the click
            // target).
            window.Left = mousePos.X + 8;
            window.Top = mousePos.Y + 8;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Owner = owner;
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        window.Show();
    }

    /// <summary>
    /// Returns the first WPF Window with a live presentation source
    /// (i.e., shown and not yet closed), or <see langword="null"/> when
    /// no such Window exists. Prefers <see cref="Application.MainWindow"/>
    /// when usable; otherwise scans <see cref="Application.Windows"/>.
    /// The PresentationSource check is the reliable
    /// "this-Hwnd-is-alive" test — relying on
    /// <see cref="Application.MainWindow"/> alone is brittle because
    /// WPF auto-clears it on the original main window's close but does
    /// not auto-reassign it when a new top-level Window is shown.
    /// </summary>
    private static Window? FindOwnerWindow()
    {
        var app = Application.Current;
        if (app is null) return null;

        if (app.MainWindow is { } main
            && PresentationSource.FromVisual(main) is HwndSource)
        {
            return main;
        }

        foreach (Window w in app.Windows)
        {
            if (PresentationSource.FromVisual(w) is HwndSource)
            {
                return w;
            }
        }

        return null;
    }
}
