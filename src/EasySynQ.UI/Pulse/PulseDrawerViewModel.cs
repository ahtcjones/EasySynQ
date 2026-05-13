using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.Logging;

namespace EasySynQ.UI.Pulse;

/// <summary>
/// View model for the Pulse slide-out drawer. Owns the drawer's
/// open/closed state, the rendered tile collection, the
/// severity-based counts that drive the topbar's pulse button, and
/// the refresh command that pulls fresh tiles from
/// <see cref="IPulseSource"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Refresh policy.</b> The drawer does NOT auto-refresh on
/// <see cref="IsOpen"/> transitions. The host calls
/// <see cref="RefreshTilesCommand"/> once on startup so the topbar
/// counts populate; subsequent refreshes are explicit (a refresh
/// button in the drawer header). This avoids surprising the user
/// with stale-vs-fresh swaps mid-view and keeps the refresh path a
/// single deliberate gesture.
/// </para>
/// <para>
/// <b>Atomic replacement.</b> <see cref="RefreshTilesCommand"/>
/// clears <see cref="Tiles"/> and re-adds the fetched set in one
/// synchronous block. Subscribers see the
/// <see cref="ObservableCollection{T}"/>'s Reset-like sequence
/// (clear, then adds) without an intervening await, so the
/// post-refresh state is the new set — not the old set plus the
/// new set.
/// </para>
/// </remarks>
public partial class PulseDrawerViewModel : ObservableObject
{
    private readonly IPulseSource _pulseSource;
    private readonly ILogger<PulseDrawerViewModel> _logger;

    /// <summary>Constructs the drawer view model.</summary>
    /// <param name="pulseSource">Tile source. Must not be null.</param>
    /// <param name="logger">Diagnostic logger. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when any
    /// argument is <see langword="null"/>.</exception>
    public PulseDrawerViewModel(IPulseSource pulseSource, ILogger<PulseDrawerViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(pulseSource);
        ArgumentNullException.ThrowIfNull(logger);
        _pulseSource = pulseSource;
        _logger = logger;

        Tiles = new ObservableCollection<PulseTile>();

        // CollectionChanged drives the per-severity count notifications
        // so the topbar's bound RedCount / AmberCount can rebind when
        // the tile set changes.
        Tiles.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(RedCount));
            OnPropertyChanged(nameof(AmberCount));
            OnPropertyChanged(nameof(GreenCount));
        };

        // Pre-grouped view for the drawer's ItemsControl. Grouping
        // is by Category; ordering within a group is whatever the
        // collection order is at refresh time (publishers are
        // expected to emit by severity when that matters).
        var src = new CollectionViewSource { Source = Tiles };
        src.GroupDescriptions.Add(new PropertyGroupDescription(nameof(PulseTile.Category)));
        TilesView = src.View;
    }

    /// <summary>Whether the drawer is currently visible.</summary>
    [ObservableProperty]
    private bool _isOpen;

    /// <summary>The current tile set. Observable so the drawer's
    /// ItemsControl reacts to refresh. Owned (not replaced) so XAML
    /// bindings stay attached across refreshes.</summary>
    public ObservableCollection<PulseTile> Tiles { get; }

    /// <summary>
    /// Pre-grouped <see cref="ICollectionView"/> over
    /// <see cref="Tiles"/>, grouping by
    /// <see cref="PulseTile.Category"/>. Bound by the drawer view's
    /// ItemsControl.
    /// </summary>
    public ICollectionView TilesView { get; }

    /// <summary>Number of red-severity tiles currently in <see cref="Tiles"/>.</summary>
    public int RedCount => Tiles.Count(t => t.Severity == PulseSeverity.Red);

    /// <summary>Number of amber-severity tiles currently in <see cref="Tiles"/>.</summary>
    public int AmberCount => Tiles.Count(t => t.Severity == PulseSeverity.Amber);

    /// <summary>Number of green-severity (reverse-pulse) tiles currently in <see cref="Tiles"/>.</summary>
    public int GreenCount => Tiles.Count(t => t.Severity == PulseSeverity.Green);

    /// <summary>Flips <see cref="IsOpen"/>.</summary>
    [RelayCommand]
    private void ToggleDrawer()
    {
        IsOpen = !IsOpen;
    }

    /// <summary>
    /// Sets <see cref="IsOpen"/> to <see langword="false"/>. Distinct
    /// command (rather than just reusing
    /// <see cref="ToggleDrawerCommand"/>) so the close-X / backdrop
    /// gesture is unambiguous regardless of current state — calling
    /// Toggle on an already-closed drawer would open it, which the
    /// backdrop should never do.
    /// </summary>
    [RelayCommand]
    private void CloseDrawer()
    {
        IsOpen = false;
    }

    /// <summary>
    /// Replaces <see cref="Tiles"/> with the source's current tile
    /// set. Clear-then-add inside one synchronous block; see type
    /// remarks for the atomicity guarantee.
    /// </summary>
    [RelayCommand]
    private async Task RefreshTilesAsync(CancellationToken cancellationToken)
    {
        var fresh = await _pulseSource
            .GetTilesAsync(cancellationToken)
            .ConfigureAwait(true);

        Tiles.Clear();
        foreach (var tile in fresh)
        {
            Tiles.Add(tile);
        }

        LogPulseRefreshed(Tiles.Count, RedCount, AmberCount, GreenCount);
    }

    /// <summary>
    /// Source-generated <see cref="LogLevel.Debug"/> emit for the
    /// post-refresh tile snapshot. Uses the
    /// <see cref="LoggerMessageAttribute"/> pattern (CA1848) so the
    /// template is compiled once; matches the precedent set by
    /// <c>LoginViewModel</c> and <c>MainShellViewModel</c>.
    /// </summary>
    [LoggerMessage(
        EventId = 3001,
        Level = LogLevel.Debug,
        Message = "Pulse drawer refreshed: {TileCount} tiles ({RedCount} red, {AmberCount} amber, {GreenCount} green)")]
    private partial void LogPulseRefreshed(int tileCount, int redCount, int amberCount, int greenCount);
}
