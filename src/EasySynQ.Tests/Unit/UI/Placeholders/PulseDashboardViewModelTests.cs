using AwesomeAssertions;

using EasySynQ.UI.Placeholders;

using Xunit;

namespace EasySynQ.Tests.Unit.UI.Placeholders;

public class PulseDashboardViewModelTests
{
    [Fact]
    public void HasUnsavedChanges_AlwaysFalse()
    {
        var sut = new PulseDashboardViewModel();
        sut.HasUnsavedChanges.Should().BeFalse("a dashboard view has nothing to discard");
    }

    [Fact]
    public async Task ConfirmDiscardAsync_AlwaysReturnsTrueAsync()
    {
        var sut = new PulseDashboardViewModel();
        var result = await sut.ConfirmDiscardAsync(CancellationToken.None);
        result.Should().BeTrue();
    }
}
