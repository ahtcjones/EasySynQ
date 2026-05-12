namespace EasySynQ.Domain.Enums;

/// <summary>
/// Retention tier governing how long a snapshot is retained, per
/// SPEC §3.3.
/// </summary>
public enum SnapshotTier
{
    /// <summary>Daily snapshot. Retained 90 days.</summary>
    Daily,

    /// <summary>Weekly snapshot (taken Sunday). Retained one year.</summary>
    Weekly,

    /// <summary>
    /// Monthly snapshot (taken on the 1st of the month). Retained
    /// indefinitely (configurable cap).
    /// </summary>
    Monthly,
}
