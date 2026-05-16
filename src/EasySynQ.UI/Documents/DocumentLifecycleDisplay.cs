using System.Globalization;

using EasySynQ.Domain.Enums;

namespace EasySynQ.UI.Documents;

/// <summary>
/// Shared helper for rendering <see cref="DocumentLifecycle"/> +
/// <c>EffectiveFromUtc</c> as a human-readable string. Used by both
/// the Document list view's row projection and the Document detail
/// view's status display.
/// </summary>
/// <remarks>
/// Per ADR 0008 §"Effective dating on DocumentRevision" plus the
/// C3 handoff's "stored state wins" lesson: the "Active" sub-state
/// is derived at read time from <c>Lifecycle == Approved</c> AND
/// <c>EffectiveFromUtc</c> compared to the supplied <c>asOfUtc</c>.
/// During the gap between approval and effective date, the display
/// surfaces <c>"Approved (effective YYYY-MM-DD)"</c> instead of
/// pretending the document is currently Active.
/// </remarks>
public static class DocumentLifecycleDisplay
{
    /// <summary>
    /// Renders the supplied lifecycle + effective-date pair as a
    /// human-readable string at the given as-of instant.
    /// </summary>
    /// <param name="lifecycle">Stored lifecycle enum value.</param>
    /// <param name="effectiveFromUtc">Stored
    /// <c>EffectiveFromUtc</c> (nullable).</param>
    /// <param name="asOfUtc">UTC instant to evaluate "Active" against —
    /// typically <c>IClock.UtcNow</c>.</param>
    /// <returns>The display string.</returns>
    public static string Format(
        DocumentLifecycle lifecycle,
        DateTime? effectiveFromUtc,
        DateTime asOfUtc)
    {
        return lifecycle switch
        {
            DocumentLifecycle.Draft => "Draft",
            DocumentLifecycle.InReview => "In Review",
            DocumentLifecycle.Approved when IsActive(effectiveFromUtc, asOfUtc) => "Active",
            DocumentLifecycle.Approved => string.Format(
                CultureInfo.InvariantCulture,
                "Approved (effective {0:yyyy-MM-dd})",
                effectiveFromUtc!.Value),
            DocumentLifecycle.Superseded => "Superseded",
            DocumentLifecycle.Archived => "Archived",
            _ => lifecycle.ToString(),
        };
    }

    private static bool IsActive(DateTime? effectiveFromUtc, DateTime asOfUtc) =>
        effectiveFromUtc is null || effectiveFromUtc.Value <= asOfUtc;
}
