using EasySynQ.UI.Documents.List;
using EasySynQ.UI.Placeholders;

using Microsoft.Extensions.DependencyInjection;

namespace EasySynQ.UI.Navigation;

/// <summary>
/// Single seam that maps a <see cref="NavigationItem"/> to the view
/// model that renders in the shell's content area.
/// </summary>
/// <remarks>
/// <para>
/// As of Phase 2 C6a, the factory is an instance class with an
/// <see cref="IServiceProvider"/> dependency rather than a static
/// method. The change is driven by real module view models that need
/// dependency injection (<see cref="DocumentListViewModel"/> for
/// <c>"governance.documents"</c>); the static-method shape worked
/// only while every produced view model was constructible without
/// services. The shell view model takes the factory as a constructor
/// dependency and invokes <see cref="CreateContentFor"/> instead of
/// the previous static call.
/// </para>
/// <para>
/// The branching logic itself is unchanged: hard-coded mapping from
/// <see cref="NavigationItem.Id"/> to view-model resolution. New
/// branches land one-per-phase as module surfaces ship.
/// <c>NavigationContentFactoryTests</c> pins the current behavior so
/// a future regression is loud.
/// </para>
/// </remarks>
public sealed class NavigationContentFactory
{
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Constructs the factory over a DI service provider. Singleton
    /// lifetime — the factory itself is stateless; it resolves a
    /// fresh view model per <see cref="CreateContentFor"/> call from
    /// the supplied provider.
    /// </summary>
    /// <param name="serviceProvider">DI service provider. Must not be
    /// <see langword="null"/>.</param>
    public NavigationContentFactory(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Creates the content view model for <paramref name="item"/>.
    /// </summary>
    /// <param name="item">Navigation entry the shell is switching to.
    /// Must not be <see langword="null"/>.</param>
    /// <returns>The view model to render in the content area.</returns>
    /// <exception cref="ArgumentNullException">Thrown when
    /// <paramref name="item"/> is <see langword="null"/>.</exception>
    public object CreateContentFor(NavigationItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return item.Id switch
        {
            "pulse.dashboard" => new PulseDashboardViewModel(),
            "governance.documents" => _serviceProvider.GetRequiredService<DocumentListViewModel>(),
            _ => new ComingSoonViewModel(item),
        };
    }
}
