namespace EasySynQ.UI.Placeholders;

/// <summary>
/// Hardcoded one-sentence descriptions per navigation entry, used by
/// <see cref="ComingSoonViewModel"/> to render the "what this module
/// will do" body on each placeholder view. The dictionary is the
/// single source of truth for these strings — anyone adding a new
/// <c>NavigationCatalog</c> entry must also add a description here.
/// </summary>
/// <remarks>
/// <see cref="DescribeOrThrow"/> throws on an unknown id rather than
/// returning a generic fallback. Reasoning: descriptions are a
/// development-time mapping; a missing entry indicates the catalog
/// grew without updating this file, and failing loudly during the
/// placeholder VM's construction is the right signal. Tests pin
/// this behavior.
/// </remarks>
internal static class ComingSoonDescriptions
{
    private static readonly Dictionary<string, string> Descriptions = new(StringComparer.Ordinal)
    {
        ["governance.documents"]         = "Controlled procedures, work instructions, and forms with revision history. Source of truth for what's required on a job.",
        ["governance.risk"]              = "Risk register with likelihood/impact scoring, ownership, and review cadence. Identify and track what could go wrong before it does.",
        ["governance.competency"]        = "Operator skills matrix tied to controlled procedures, with qualification expiry and production-lock cascades when a required qualification lapses.",
        ["governance.audits"]            = "Internal audit planning, evidence collection, finding management, and clause-coverage tracking against ISO 9001 and any enabled compliance modules.",
        ["governance.management-review"] = "Quarterly management review packets with input rollups, decisions, action items, and the signature chain auditors expect to see.",
        ["operations.suppliers"]         = "Supplier qualification, audit scheduling, scorecards, and approved-source enforcement for incoming raw materials.",
        ["operations.assets"]            = "Asset register with preventive-maintenance scheduling, calibration tracking, and chart-recorder linkage for furnaces and quench tanks.",
        ["operations.material"]          = "Lot-level traceability from receipt through job consumption, with full backward and forward traversal for any unit of material.",
        ["operations.production"]        = "Job travelers from intake to release, with three QA signature gates (chart review, inspection, CoC issuance), chart import, lab inspection, and Certificate of Conformance generation.",
        ["quality.ncr"]                  = "Nonconformance reports — disposition, segregation, customer-concession routing, and the link back to the offending job, lot, or asset.",
        ["quality.capa"]                 = "Corrective and preventive actions with effectiveness verification, evidence packaging, and the 30-day verification-window watcher.",
        ["insights.analytics"]           = "First-pass yield rollups, NCR trend analysis, asset utilization, and the data feed Management Review depends on.",
    };

    /// <summary>
    /// Looks up the description for <paramref name="navigationItemId"/>.
    /// </summary>
    /// <param name="navigationItemId">Stable id from <c>NavigationCatalog</c>.</param>
    /// <returns>The hardcoded description string.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when no description
    /// is registered for the supplied id — see type remarks.</exception>
    public static string DescribeOrThrow(string navigationItemId)
    {
        if (Descriptions.TryGetValue(navigationItemId, out var description))
        {
            return description;
        }
        throw new KeyNotFoundException(
            $"No ComingSoon description registered for navigation id '{navigationItemId}'. "
            + $"Add an entry to {nameof(ComingSoonDescriptions)} when introducing a catalog entry.");
    }
}
