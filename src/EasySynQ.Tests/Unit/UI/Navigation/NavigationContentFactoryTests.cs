using AwesomeAssertions;

using EasySynQ.Domain.Entities.Documents;
using EasySynQ.Services.Abstractions;
using EasySynQ.Services.Documents;
using EasySynQ.Services.Time;
using EasySynQ.UI.Documents;
using EasySynQ.UI.Documents.CreateDocument;
using EasySynQ.UI.Documents.Detail;
using EasySynQ.UI.Documents.List;
using EasySynQ.UI.Navigation;
using EasySynQ.UI.Placeholders;

using Microsoft.Extensions.DependencyInjection;

using Moq;

using Xunit;

namespace EasySynQ.Tests.Unit.UI.Navigation;

/// <summary>
/// Unit tests for the C6a-instance-shape <see cref="NavigationContentFactory"/>.
/// Pins the branching mapping from <see cref="NavigationItem.Id"/> to the
/// produced view-model type.
/// </summary>
public class NavigationContentFactoryTests
{
    private static NavigationContentFactory NewFactory(IServiceProvider? provider = null)
        => new(provider ?? new ServiceCollection().BuildServiceProvider());

    private static ServiceProvider DocumentListResolver()
    {
        // Real DI graph for the governance.documents branch — every
        // DocumentListViewModel dependency mocked. The factory test
        // only needs the resolution to succeed; the resulting VM is
        // never exercised here (its own tests cover behavior).
        var services = new ServiceCollection();
        services.AddSingleton(Mock.Of<IDocumentRepository>());
        services.AddSingleton(Mock.Of<IDocumentRevisionRepository>());
        services.AddSingleton(Mock.Of<IUserRepository>());
        services.AddSingleton(Mock.Of<ICurrentUserAccessor>());
        services.AddSingleton(Mock.Of<ICreateDocumentPrompter>());
        services.AddSingleton(Mock.Of<IDocumentLifecycleService>());
        services.AddSingleton(Mock.Of<IClock>());
        services.AddSingleton(Mock.Of<EasySynQ.UI.LockInspector.ILockInspectorPrompter>());
        services.AddSingleton<Func<Document, DocumentDetailViewModel>>(
            _ => _ => throw new NotImplementedException("test stub — not invoked"));
        services.AddTransient<DocumentListViewModel>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void CreateContentFor_PulseDashboard_ReturnsPulseDashboardViewModel()
    {
        var pulse = NavigationCatalog.AllItems.Single(i => i.Id == "pulse.dashboard");

        var result = NewFactory().CreateContentFor(pulse);

        result.Should().BeOfType<PulseDashboardViewModel>();
    }

    [Fact]
    public void CreateContentFor_GovernanceDocuments_ResolvesDocumentListViewModel()
    {
        var item = NavigationCatalog.AllItems.Single(i => i.Id == "governance.documents");
        var factory = NewFactory(DocumentListResolver());

        var result = factory.CreateContentFor(item);

        result.Should().BeOfType<DocumentListViewModel>();
    }

    [Fact]
    public void CreateContentFor_AnyOtherItem_ReturnsComingSoonViewModelWithThatItem()
    {
        // Pick an entry that is not pulse.dashboard and not
        // governance.documents — those have explicit factory
        // branches. Everything else still falls through to the
        // ComingSoonViewModel placeholder until its owning phase
        // ships.
        var item = NavigationCatalog.AllItems.Single(i => i.Id == "governance.risk");

        var result = NewFactory().CreateContentFor(item);

        var coming = result.Should().BeOfType<ComingSoonViewModel>().Subject;
        coming.DisplayName.Should().Be(item.DisplayName);
        coming.TargetPhase.Should().Be(item.TargetPhase);
    }
}
