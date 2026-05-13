namespace EasySynQ.UI.Pulse;

/// <summary>
/// Consumer-side contract for the Pulse drawer's tile data. The
/// drawer view model asks for the current tile set on demand; the
/// source decides how to assemble it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Aggregation is intentionally not part of this surface.</b>
/// Future implementations will gather tiles from several publishers
/// (the NCR module emits red/amber tiles for open / pending NCRs,
/// the calibration module emits overdue-cal tiles, Document
/// Controller emits compatibility-review tiles, etc.) and merge
/// them — but the publisher-side fan-in pattern is a service-layer
/// concern that ships with the modules themselves. From the
/// drawer's perspective the source is a single async call that
/// returns the current snapshot. That keeps the consumer stable
/// across the implementation evolution.
/// </para>
/// <para>
/// E2.3 ships <see cref="MockPulseSource"/>, a hardcoded
/// implementation that mirrors the prototype's tile data. Real
/// publishers come online with their owning phases (Phase 5 for
/// calibration, Phase 8 for NCR/CAPA, etc.).
/// </para>
/// </remarks>
public interface IPulseSource
{
    /// <summary>
    /// Returns the current set of Pulse tiles. Order within the
    /// returned list is not significant to the drawer — the view
    /// model regroups by <see cref="PulseTile.Category"/> for
    /// rendering — but stable order is helpful for diagnostic logs.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<PulseTile>> GetTilesAsync(CancellationToken cancellationToken);
}
