using System.Windows.Controls.Primitives;

using EasySynQ.Services.Abstractions;
using EasySynQ.Services.LockReasons;

namespace EasySynQ.UI.LockInspector;

/// <summary>
/// Production <see cref="ILockInspectorPrompter"/>. Constructs a fresh
/// <see cref="LockInspectorViewModel"/> per call, hosts the
/// <see cref="LockInspectorPopover"/> inside a WPF
/// <see cref="Popup"/> primitive, and shows the popup near the cursor
/// (ADR 0012 C7b). The Popup auto-closes on click-outside via
/// <c>StaysOpen=False</c>; the prompter holds a reference to the
/// active popup so it stays alive while shown and so a subsequent
/// open closes the prior popover before showing the new one.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a Popup primitive, not a Window.</b> An earlier draft of
/// this prompter (and the original ADR 0012 framing) used a
/// chromeless top-level Window with auto-close on
/// <c>Deactivated</c>. That shape interacts poorly with the WPF
/// activation model when called after an <c>await</c>: the
/// synchronous user-input context that allows focus-stealing has
/// already cleared by the time <c>Show()</c> runs, the OS refuses to
/// activate the chromeless Window, and <c>Deactivated</c> fires
/// immediately on Show — the popover closes before the user sees it.
/// The Popup primitive sidesteps this entirely: it does not steal
/// focus, click-outside dismissal is handled by <c>StaysOpen=False</c>'s
/// internal mouse-capture hook, and positioning via
/// <see cref="PlacementMode.MousePoint"/> works regardless of
/// activation state.
/// </para>
/// <para>
/// <b>Single-popover-at-a-time.</b> The prompter holds a reference to
/// the active <see cref="Popup"/> in <c>_currentPopup</c>. A second
/// open call closes the prior popup before constructing the new one.
/// The Popup's <see cref="Popup.Closed"/> event clears the reference
/// so a popup auto-dismissed by user click is also reclaimed.
/// </para>
/// </remarks>
public sealed class LockInspectorPrompter : ILockInspectorPrompter
{
    private readonly ILockReasonRepository _lockReasons;
    private readonly ILockReasonResolverRegistry _registry;

    // Keeps the active popup alive while it is shown and lets a
    // subsequent OpenAsync close the prior popover before showing a
    // new one. Cleared in the popup's Closed handler.
    private Popup? _currentPopup;

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

        // Close any active popup so we never have two open at once.
        if (_currentPopup is not null)
        {
            _currentPopup.IsOpen = false;
        }

        var vm = new LockInspectorViewModel(
            lockedEntityType,
            lockedEntityId,
            _lockReasons,
            _registry);

        var content = new LockInspectorPopover
        {
            DataContext = vm,
        };

        var popup = new Popup
        {
            Child = content,
            Placement = PlacementMode.MousePoint,
            StaysOpen = false,
            AllowsTransparency = true,
            PopupAnimation = PopupAnimation.Fade,
        };

        // Self-clearing reference so a user-dismissed popup is
        // reclaimed (the prompter does not have to know whether the
        // popup is closed by IsOpen=false above or by the user
        // clicking outside).
        popup.Closed += (_, _) =>
        {
            if (ReferenceEquals(_currentPopup, popup))
            {
                _currentPopup = null;
            }
        };

        _currentPopup = popup;

        // Open BEFORE awaiting the load so the popup appears
        // immediately with the "Loading…" affordance. The chain
        // populates when LoadAsync completes; the popup's open
        // state is independent of the load timing.
        popup.IsOpen = true;

        await vm.LoadAsync(cancellationToken);
    }
}
