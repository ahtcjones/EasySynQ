using AwesomeAssertions;

using EasySynQ.UI.Navigation;
using EasySynQ.UI.Placeholders;

using Xunit;

namespace EasySynQ.Tests.Unit.UI.Navigation;

public class NavigationContentFactoryTests
{
    [Fact]
    public void CreateContentFor_PulseDashboard_ReturnsPulseDashboardViewModel()
    {
        var pulse = NavigationCatalog.AllItems.Single(i => i.Id == "pulse.dashboard");

        var result = NavigationContentFactory.CreateContentFor(pulse);

        result.Should().BeOfType<PulseDashboardViewModel>();
    }

    [Fact]
    public void CreateContentFor_AnyOtherItem_ReturnsComingSoonViewModelWithThatItem()
    {
        var item = NavigationCatalog.AllItems.Single(i => i.Id == "governance.documents");

        var result = NavigationContentFactory.CreateContentFor(item);

        var coming = result.Should().BeOfType<ComingSoonViewModel>().Subject;
        coming.DisplayName.Should().Be(item.DisplayName);
        coming.TargetPhase.Should().Be(item.TargetPhase);
    }
}
