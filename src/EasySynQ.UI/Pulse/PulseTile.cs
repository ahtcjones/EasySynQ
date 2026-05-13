namespace EasySynQ.UI.Pulse;

/// <summary>
/// One alert / good-news entry in the Pulse drawer. Carries enough
/// state to render the tile and route a click into the underlying
/// view; the navigation routing is deferred until E2.4 when
/// placeholder views land, but the <see cref="Id"/> contract is in
/// place now so click handlers can grow into it without reshaping
/// the type.
/// </summary>
/// <remarks>
/// <para>
/// <b>Identity.</b> Equality is by <see cref="Id"/>. Two tiles with
/// the same id but different field values are still considered the
/// same tile — this is what lets future incremental-refresh logic
/// diff "old tile set" vs "new tile set" by id without considering
/// headline/count drift as a new tile. The current
/// <c>PulseDrawerViewModel</c> uses clear-and-add rather than diff,
/// but the equality contract is the one the diff path will rely on
/// when it lands.
/// </para>
/// <para>
/// <b>Reverse-pulse.</b> When <see cref="Severity"/> is
/// <see cref="PulseSeverity.Green"/>, the tile is rendered as a
/// good-news tile per SPEC §4.2. The drawer's tile template applies
/// the lighter "green-news" tint and treats <see cref="Count"/> as
/// optional context (e.g. "47d") rather than as a numeric badge.
/// </para>
/// </remarks>
public sealed class PulseTile : IEquatable<PulseTile>
{
    /// <summary>
    /// Stable identifier for this tile across refreshes. Should be
    /// derived from the underlying source (e.g. <c>"ncr.overdue"</c>,
    /// <c>"calibration.load-tc1"</c>). Must not be null, empty, or
    /// whitespace.
    /// </summary>
    public string Id { get; }

    /// <summary>Category grouping. Drives drawer-section assignment.</summary>
    public PulseCategory Category { get; }

    /// <summary>Severity tier. <see cref="PulseSeverity.Green"/> is reverse-pulse.</summary>
    public PulseSeverity Severity { get; }

    /// <summary>
    /// Primary line of the tile, e.g. <c>"3 overdue calibrations"</c>.
    /// Must not be null, empty, or whitespace.
    /// </summary>
    public string Headline { get; }

    /// <summary>
    /// Secondary line, e.g.
    /// <c>"Furnace 2 quench thermocouple, 11 days overdue"</c>, or
    /// <see langword="null"/> if no secondary context.
    /// </summary>
    public string? Detail { get; }

    /// <summary>
    /// Numeric badge for the tile. <see langword="null"/> on
    /// reverse-pulse tiles that do not carry a count, or on any tile
    /// that's purely textual. Renderers may also display this as
    /// "47d" / "100%" for green tiles where the source uses a
    /// short non-numeric string — the type stays <see cref="int"/>?
    /// for clarity; consumers wanting a non-numeric tail use
    /// <see cref="Detail"/>.
    /// </summary>
    public int? Count { get; }

    /// <summary>Constructs a tile.</summary>
    /// <exception cref="ArgumentException">Thrown when <paramref name="id"/>
    /// or <paramref name="headline"/> is null/empty/whitespace.</exception>
    public PulseTile(
        string id,
        PulseCategory category,
        PulseSeverity severity,
        string headline,
        string? detail,
        int? count)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(headline);
        Id = id;
        Category = category;
        Severity = severity;
        Headline = headline;
        Detail = detail;
        Count = count;
    }

    /// <inheritdoc />
    public bool Equals(PulseTile? other) =>
        other is not null && string.Equals(Id, other.Id, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is PulseTile other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => Id.GetHashCode(StringComparison.Ordinal);
}
