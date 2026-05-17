namespace EasySynQ.UI.LockInspector;

/// <summary>
/// Surface for opening the lock-inspector popover (ADR 0012 C7b).
/// View models that surface lock affordances inject this and call
/// <see cref="OpenAsync"/> from a <c>RelayCommand</c> wired to the
/// click target. The prompter constructs the
/// <see cref="LockInspectorViewModel"/>, runs the resolver, presents
/// the popover, and handles auto-dismiss — the calling VM does not
/// need to know any of those details.
/// </summary>
public interface ILockInspectorPrompter
{
    /// <summary>
    /// Opens the inspector for the supplied entity reference. The
    /// popover is non-blocking — the returned task completes after
    /// the popover has been shown (not after it closes). Click-outside
    /// dismissal is handled by the popover's <c>Deactivated</c> hook.
    /// </summary>
    /// <param name="lockedEntityType">Canonical type string (see
    /// <see cref="EasySynQ.Domain.LockedEntityTypes"/>).</param>
    /// <param name="lockedEntityId">Canonical string-form id of the
    /// entity whose lock state is being inspected.</param>
    /// <param name="cancellationToken">Cancellation token honored by
    /// the internal <see cref="LockInspectorViewModel.LoadAsync"/>
    /// call; cancelling here aborts the resolver lookup but does
    /// not close an already-presented popover.</param>
    Task OpenAsync(
        string lockedEntityType,
        string lockedEntityId,
        CancellationToken cancellationToken);
}
