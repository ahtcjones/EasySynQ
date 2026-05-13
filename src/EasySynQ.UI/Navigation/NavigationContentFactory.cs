using EasySynQ.UI.Placeholders;

namespace EasySynQ.UI.Navigation;

/// <summary>
/// Single seam that maps a <see cref="NavigationItem"/> to the view
/// model that renders in the shell's content area.
/// </summary>
/// <remarks>
/// <para>
/// This is the place real module view models replace placeholders as
/// their owning phases ship. When Phase 2 lands, the
/// <c>"governance.documents"</c> branch swaps from
/// <see cref="ComingSoonViewModel"/> to a real
/// <c>DocumentsViewModel</c>; the rest stays unchanged. The shell
/// view model has no module-specific knowledge — it asks the factory
/// for the content and binds whatever it gets to
/// <c>CurrentContent</c>.
/// </para>
/// <para>
/// Today the factory branches only on
/// <c>"pulse.dashboard"</c> (returns
/// <see cref="PulseDashboardViewModel"/>) versus everything else
/// (returns <see cref="ComingSoonViewModel"/>). New branches land
/// one-per-phase; <c>NavigationContentFactoryTests</c> pins the
/// current behavior so a future regression is loud.
/// </para>
/// </remarks>
public static class NavigationContentFactory
{
    /// <summary>
    /// Creates the content view model for <paramref name="item"/>.
    /// </summary>
    /// <param name="item">Navigation entry the shell is switching to.
    /// Must not be <see langword="null"/>.</param>
    /// <returns>The view model to render in the content area.</returns>
    /// <exception cref="ArgumentNullException">Thrown when
    /// <paramref name="item"/> is <see langword="null"/>.</exception>
    public static object CreateContentFor(NavigationItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return item.Id switch
        {
            "pulse.dashboard" => new PulseDashboardViewModel(),
            _ => new ComingSoonViewModel(item),
        };
    }
}
