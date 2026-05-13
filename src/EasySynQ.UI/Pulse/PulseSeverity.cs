namespace EasySynQ.UI.Pulse;

/// <summary>
/// Severity classification for a Pulse tile. Mirrors the SPEC §4.4
/// severity discipline (red / amber / green) with one purposeful
/// omission: blue/info is not included because Pulse surfaces
/// alerts and good news, not in-progress state.
/// </summary>
/// <remarks>
/// <see cref="Green"/> is the load-bearing case: a green-severity
/// tile is rendered as a "reverse-pulse" / good-news tile per
/// SPEC §4.2. The drawer's tile template branches its visual tint
/// and the absence of a count badge on this case. Publishers should
/// emit green tiles only when the category has been clean for the
/// configured window — see <see cref="PulseTile"/>.
/// </remarks>
public enum PulseSeverity
{
    /// <summary>Critical / overdue / failed.</summary>
    Red,

    /// <summary>Review needed / approaching due.</summary>
    Amber,

    /// <summary>Good-news / reverse-pulse tile. See type remarks.</summary>
    Green,
}
