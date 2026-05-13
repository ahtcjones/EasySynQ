using EasySynQ.UI.Navigation;

namespace EasySynQ.UI.Placeholders;

/// <summary>
/// View model for the Pulse Dashboard placeholder content area. The
/// dashboard is austere by design in E2.4 — live alerts and good-news
/// tiles already live in the slide-out Pulse drawer; a richer
/// dashboard with summary widgets (FPY trend, NCR Pareto, recent
/// activity) is on the Phase 1 polish list.
/// </summary>
/// <remarks>
/// Implements <see cref="IDirtyStateAware"/> so the shell's
/// dirty-state guard treats this view uniformly with every other
/// content view. There is nothing to discard on a dashboard.
/// </remarks>
public sealed class PulseDashboardViewModel : IDirtyStateAware
{
    /// <inheritdoc />
    public bool HasUnsavedChanges { get; }

    /// <inheritdoc />
    public Task<bool> ConfirmDiscardAsync(CancellationToken cancellationToken)
        => Task.FromResult(true);
}
