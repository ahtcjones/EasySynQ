namespace EasySynQ.UI.Navigation;

/// <summary>
/// One entry in the shell's navigation tree. Reference semantics —
/// <see cref="MainShellViewModel.SelectedItem"/> comparisons rely on
/// reference equality, which means the catalog's singleton instances
/// are the only objects that satisfy the comparison.
/// </summary>
/// <remarks>
/// Construction is the only writable surface; every property is
/// initialized once and read-only thereafter. The catalog
/// (<see cref="NavigationCatalog"/>) is the authoritative source — no
/// code should construct ad-hoc <see cref="NavigationItem"/> instances
/// outside of test scaffolding.
/// </remarks>
public sealed class NavigationItem
{
    /// <summary>
    /// Stable identifier, e.g. <c>"pulse.dashboard"</c>,
    /// <c>"governance.documents"</c>. Used for diagnostics, audit
    /// references (eventually), and the
    /// <see cref="NavigationCatalog"/>'s lookup discipline. Must not be
    /// null, empty, or whitespace.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// User-facing label rendered in the navigation tree. Must not be
    /// null, empty, or whitespace.
    /// </summary>
    public string DisplayName { get; }

    /// <summary>The section this item belongs to.</summary>
    public NavigationSection Section { get; }

    /// <summary>
    /// SPEC §9 deployment-roadmap phase number that introduces this
    /// item. Used by placeholder views (E2.4) to render the
    /// "shipped in phase N" hint.
    /// </summary>
    public int TargetPhase { get; }

    /// <summary>
    /// <see langword="false"/> until the target phase ships and the
    /// real view is wired up; controls click affordance and visual
    /// dimming in the navigation tree.
    /// </summary>
    public bool IsAvailable { get; }

    /// <summary>Constructs a navigation entry.</summary>
    /// <param name="id">Stable identifier.</param>
    /// <param name="displayName">User-facing label.</param>
    /// <param name="section">Section grouping.</param>
    /// <param name="targetPhase">SPEC §9 phase number.</param>
    /// <param name="isAvailable">Whether the underlying view is live.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="id"/>
    /// or <paramref name="displayName"/> is null, empty, or whitespace.</exception>
    public NavigationItem(
        string id,
        string displayName,
        NavigationSection section,
        int targetPhase,
        bool isAvailable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        Id = id;
        DisplayName = displayName;
        Section = section;
        TargetPhase = targetPhase;
        IsAvailable = isAvailable;
    }
}
