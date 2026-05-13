using AwesomeAssertions;

using EasySynQ.UI.Navigation;
using EasySynQ.UI.Placeholders;

using Xunit;

namespace EasySynQ.Tests.Unit.UI.Placeholders;

public class ComingSoonViewModelTests
{
    private static readonly NavigationItem KnownItem =
        NavigationCatalog.AllItems.Single(i => i.Id == "governance.documents");

    [Fact]
    public void Constructor_NullItem_Throws()
    {
        var act = () => new ComingSoonViewModel(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("item");
    }

    [Fact]
    public void DisplayName_ReturnsItemDisplayName()
    {
        var sut = new ComingSoonViewModel(KnownItem);
        sut.DisplayName.Should().Be(KnownItem.DisplayName);
    }

    [Fact]
    public void TargetPhase_ReturnsItemTargetPhase()
    {
        var sut = new ComingSoonViewModel(KnownItem);
        sut.TargetPhase.Should().Be(KnownItem.TargetPhase);
    }

    [Fact]
    public void Description_KnownItemId_ReturnsHardcodedDescription()
    {
        var sut = new ComingSoonViewModel(KnownItem);
        sut.Description.Should().Be(
            "Controlled procedures, work instructions, and forms with revision history. Source of truth for what's required on a job.");
    }

    [Fact]
    public void Description_UnknownItemId_Throws()
    {
        // Decision: a missing description for a real catalog entry is
        // a development-time mistake (the catalog grew without a
        // matching ComingSoonDescriptions entry). Throwing makes the
        // mistake loud at the moment of ComingSoonViewModel
        // construction rather than rendering a fallback that nobody
        // sees during automated testing.
        var fake = new NavigationItem(
            "fake.unknown",
            "Fake",
            NavigationSection.Governance,
            targetPhase: 99,
            isAvailable: false);

        var act = () => new ComingSoonViewModel(fake);

        act.Should().Throw<KeyNotFoundException>()
            .WithMessage("*fake.unknown*");
    }

    [Fact]
    public void HasUnsavedChanges_AlwaysFalse()
    {
        var sut = new ComingSoonViewModel(KnownItem);
        sut.HasUnsavedChanges.Should().BeFalse("placeholder views have nothing to discard");
    }

    [Fact]
    public async Task ConfirmDiscardAsync_AlwaysReturnsTrueAsync()
    {
        var sut = new ComingSoonViewModel(KnownItem);
        var result = await sut.ConfirmDiscardAsync(CancellationToken.None);
        result.Should().BeTrue();
    }
}
