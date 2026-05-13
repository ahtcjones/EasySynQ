namespace EasySynQ.UI.Navigation;

/// <summary>
/// Authoritative list of navigation entries for the shell. The order,
/// section grouping, and target-phase numbers all derive from
/// <b>SPEC §9 (Deployment Roadmap)</b> and the bracketed
/// section tags it carries — anyone modifying this catalog must first
/// re-read SPEC §9 and verify the mapping still holds. The catalog is
/// the single load-bearing place where the SPEC's phase/section tags
/// become code.
/// </summary>
/// <remarks>
/// <para>
/// Each entry's <see cref="NavigationItem.IsAvailable"/> flag is
/// <see langword="false"/> until its target phase ships. The catalog
/// itself is shipped at Phase 1 / Chunk E2 (the shell phase) so the
/// tree structure is in place, even though only
/// <c>"pulse.dashboard"</c> is live until Phase 2 begins landing.
/// </para>
/// <para>
/// The list is exposed as <see cref="IReadOnlyList{T}"/> so consumers
/// see iteration order — the order is the rendered order of the
/// navigation tree and is part of the contract.
/// </para>
/// </remarks>
public static class NavigationCatalog
{
    /// <summary>
    /// All navigation entries in rendered order. The order is the SPEC
    /// §4.1 section order (Pulse, Governance, Operations, Quality,
    /// Insights), with items inside each section ordered by SPEC §9
    /// phase introduction (ties broken by the order they appear in the
    /// phase's bracket tag).
    /// </summary>
    public static IReadOnlyList<NavigationItem> AllItems { get; } = new NavigationItem[]
    {
        // Pulse — always-present shell affordance, no section header.
        new("pulse.dashboard", "Pulse Dashboard", NavigationSection.Pulse, targetPhase: 1, isAvailable: true),

        // Governance — Phase 2, 3, 4, 9.
        new("governance.documents",          "Documents",          NavigationSection.Governance, targetPhase: 2, isAvailable: false),
        new("governance.risk",               "Risk Register",      NavigationSection.Governance, targetPhase: 3, isAvailable: false),
        new("governance.competency",         "Competency Matrix",  NavigationSection.Governance, targetPhase: 4, isAvailable: false),
        new("governance.audits",             "Internal Audits",    NavigationSection.Governance, targetPhase: 9, isAvailable: false),
        new("governance.management-review",  "Management Review",  NavigationSection.Governance, targetPhase: 9, isAvailable: false),

        // Operations — Phase 3, 5, 6, 7.
        new("operations.suppliers",   "Suppliers",             NavigationSection.Operations, targetPhase: 3, isAvailable: false),
        new("operations.assets",      "Assets & PM",           NavigationSection.Operations, targetPhase: 5, isAvailable: false),
        new("operations.material",    "Material Traceability", NavigationSection.Operations, targetPhase: 6, isAvailable: false),
        new("operations.production",  "Production Jobs",       NavigationSection.Operations, targetPhase: 7, isAvailable: false),

        // Quality — Phase 8.
        new("quality.ncr",   "NCRs",  NavigationSection.Quality, targetPhase: 8, isAvailable: false),
        new("quality.capa",  "CAPAs", NavigationSection.Quality, targetPhase: 8, isAvailable: false),

        // Insights — Phase 10.
        new("insights.analytics", "Analytics", NavigationSection.Insights, targetPhase: 10, isAvailable: false),
    };

    /// <summary>
    /// Returns the entries in <paramref name="section"/> in their
    /// catalog order. Convenience for the navigation view's
    /// per-section rendering.
    /// </summary>
    /// <param name="section">The section to filter on.</param>
    public static IEnumerable<NavigationItem> ItemsInSection(NavigationSection section)
        => AllItems.Where(item => item.Section == section);
}
