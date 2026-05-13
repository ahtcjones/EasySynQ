using AwesomeAssertions;

using EasySynQ.UI.Pulse;

using Microsoft.Extensions.Logging;

using Moq;

using Xunit;

namespace EasySynQ.Tests.Unit.UI.Pulse;

public class PulseDrawerViewModelTests
{
    private readonly Mock<IPulseSource> _source = new(MockBehavior.Strict);
    private readonly Mock<ILogger<PulseDrawerViewModel>> _logger = new();

    private PulseDrawerViewModel BuildSut()
    {
        _logger.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        return new PulseDrawerViewModel(_source.Object, _logger.Object);
    }

    private static PulseTile Tile(string id, PulseSeverity severity, PulseCategory category = PulseCategory.Ncr) =>
        new(id, category, severity, $"Headline {id}", detail: null, count: 1);

    [Fact]
    public void ToggleDrawerCommand_WhenClosed_OpensDrawer()
    {
        var sut = BuildSut();
        sut.IsOpen.Should().BeFalse("default state is closed");

        sut.ToggleDrawerCommand.Execute(null);

        sut.IsOpen.Should().BeTrue();
    }

    [Fact]
    public void ToggleDrawerCommand_WhenOpen_ClosesDrawer()
    {
        var sut = BuildSut();
        sut.ToggleDrawerCommand.Execute(null);
        sut.IsOpen.Should().BeTrue();

        sut.ToggleDrawerCommand.Execute(null);

        sut.IsOpen.Should().BeFalse();
    }

    [Fact]
    public void CloseDrawerCommand_AlwaysSetsIsOpenFalse()
    {
        var sut = BuildSut();

        // From closed → still closed (no toggle).
        sut.CloseDrawerCommand.Execute(null);
        sut.IsOpen.Should().BeFalse();

        // From open → closed.
        sut.ToggleDrawerCommand.Execute(null);
        sut.IsOpen.Should().BeTrue();
        sut.CloseDrawerCommand.Execute(null);
        sut.IsOpen.Should().BeFalse();
    }

    [Fact]
    public async Task RefreshTilesAsync_PopulatesTilesFromSourceAsync()
    {
        var tiles = new[]
        {
            Tile("a", PulseSeverity.Red),
            Tile("b", PulseSeverity.Amber, PulseCategory.Calibration),
        };
        _source.Setup(s => s.GetTilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(tiles);

        var sut = BuildSut();
        await sut.RefreshTilesCommand.ExecuteAsync(null);

        sut.Tiles.Should().HaveCount(2);
        sut.Tiles.Select(t => t.Id).Should().BeEquivalentTo(["a", "b"]);
    }

    [Fact]
    public async Task RefreshTilesAsync_ReplacesPreviousTilesAtomicallyAsync()
    {
        var first = new[] { Tile("a", PulseSeverity.Red) };
        var second = new[] { Tile("b", PulseSeverity.Amber, PulseCategory.Calibration) };
        _source.SetupSequence(s => s.GetTilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(first)
            .ReturnsAsync(second);

        var sut = BuildSut();
        await sut.RefreshTilesCommand.ExecuteAsync(null);
        sut.Tiles.Should().HaveCount(1);
        sut.Tiles[0].Id.Should().Be("a");

        await sut.RefreshTilesCommand.ExecuteAsync(null);
        sut.Tiles.Should().HaveCount(1, "the second refresh replaces the first set rather than appending");
        sut.Tiles[0].Id.Should().Be("b");
    }

    [Fact]
    public async Task RedAmberGreenCount_ComputesFromTilesByCategoryAsync()
    {
        // The test name preserves the spec; the logic is "count by
        // severity" (Red/Amber/Green). The mixed-severity input below
        // exercises all three buckets at once.
        var tiles = new[]
        {
            Tile("r1", PulseSeverity.Red),
            Tile("r2", PulseSeverity.Red),
            Tile("a1", PulseSeverity.Amber, PulseCategory.Calibration),
            Tile("g1", PulseSeverity.Green, PulseCategory.ManagementReview),
            Tile("g2", PulseSeverity.Green, PulseCategory.Customer),
            Tile("g3", PulseSeverity.Green, PulseCategory.Traceability),
        };
        _source.Setup(s => s.GetTilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(tiles);

        var sut = BuildSut();
        await sut.RefreshTilesCommand.ExecuteAsync(null);

        sut.RedCount.Should().Be(2);
        sut.AmberCount.Should().Be(1);
        sut.GreenCount.Should().Be(3);
    }

    [Fact]
    public void Constructor_NullPulseSource_Throws()
    {
        var act = () => new PulseDrawerViewModel(null!, _logger.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("pulseSource");
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new PulseDrawerViewModel(_source.Object, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }
}
