namespace EasySynQ.UI.Navigation;

/// <summary>
/// Top-level grouping of the navigation tree, as defined by SPEC §4.1
/// ("Shell"). Every module in SPEC §9 belongs to exactly one of these
/// five sections — see <see cref="NavigationCatalog"/> for the mapping.
/// </summary>
public enum NavigationSection
{
    /// <summary>
    /// The dashboard / Pulse surface. Sits above the section headers in
    /// the navigation tree and has no header label of its own (matches
    /// the prototype's pattern).
    /// </summary>
    Pulse,

    /// <summary>
    /// Governance: Documents, Risk Register, Competency Matrix,
    /// Internal Audits, Management Review.
    /// </summary>
    Governance,

    /// <summary>
    /// Operations: Suppliers, Assets &amp; PM, Material Traceability,
    /// Production Jobs.
    /// </summary>
    Operations,

    /// <summary>
    /// Quality: NCRs, CAPAs.
    /// </summary>
    Quality,

    /// <summary>
    /// Insights: Analytics / Data Intelligence rollups.
    /// </summary>
    Insights,
}
