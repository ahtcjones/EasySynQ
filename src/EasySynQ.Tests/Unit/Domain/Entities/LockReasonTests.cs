using AwesomeAssertions;

using EasySynQ.Domain.Entities.Audit;

using Xunit;

namespace EasySynQ.Tests.Unit.Domain.Entities;

public class LockReasonTests
{
    private static LockReasonLink Link(string id, bool isTerminal = false, string? because = "because reason") =>
        new(
            tag: "Job",
            id: id,
            detail: "detail",
            navigationEntityType: null,
            navigationEntityId: null,
            because: isTerminal ? null : because,
            isTerminal: isTerminal);

    [Fact]
    public void Constructor_AcceptsValidChain()
    {
        var chain = new[]
        {
            Link("first"),
            Link("second"),
            Link("third", isTerminal: true),
        };

        var reason = new LockReason(Guid.NewGuid(), "Job", "J-2026-0847", chain);

        reason.Chain.Should().HaveCount(3);
        reason.Chain[^1].IsTerminal.Should().BeTrue();
    }

    [Fact]
    public void Constructor_AcceptsSingleTerminalLinkChain()
    {
        var chain = new[] { Link("only", isTerminal: true) };
        var reason = new LockReason(Guid.NewGuid(), "Job", "J-2026-0847", chain);
        reason.Chain.Should().ContainSingle().Which.IsTerminal.Should().BeTrue();
    }

    [Fact]
    public void Constructor_RejectsEmptyChain()
    {
        Action act = () => new LockReason(Guid.NewGuid(), "Job", "J-2026-0847", []);
        act.Should().Throw<ArgumentException>().WithMessage("*at least one link*");
    }

    [Fact]
    public void Constructor_RejectsChainWithNoTerminalLink()
    {
        var chain = new[]
        {
            Link("first"),
            Link("second"),
        };

        Action act = () => new LockReason(Guid.NewGuid(), "Job", "J-2026-0847", chain);
        act.Should().Throw<ArgumentException>().WithMessage("*exactly one terminal link*");
    }

    [Fact]
    public void Constructor_RejectsTerminalLinkInNonLastPosition()
    {
        var chain = new[]
        {
            Link("first", isTerminal: true),
            Link("second"),
        };

        Action act = () => new LockReason(Guid.NewGuid(), "Job", "J-2026-0847", chain);
        act.Should().Throw<ArgumentException>().WithMessage("*last link*");
    }

    [Fact]
    public void Constructor_RejectsNonTerminalLinkWithoutBecause()
    {
        var chain = new[]
        {
            Link("first", because: null),
            Link("second", isTerminal: true),
        };

        Action act = () => new LockReason(Guid.NewGuid(), "Job", "J-2026-0847", chain);
        act.Should().Throw<ArgumentException>().WithMessage("*Because connector*");
    }

    [Fact]
    public void Constructor_RejectsNonTerminalLinkWithWhitespaceBecause()
    {
        var chain = new[]
        {
            Link("first", because: "   "),
            Link("second", isTerminal: true),
        };

        Action act = () => new LockReason(Guid.NewGuid(), "Job", "J-2026-0847", chain);
        act.Should().Throw<ArgumentException>().WithMessage("*Because connector*");
    }

    [Fact]
    public void Constructor_RejectsNullChain()
    {
        Action act = () => new LockReason(Guid.NewGuid(), "Job", "J-2026-0847", null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void LockReasonLink_RejectsEmptyTag()
    {
        Action act = () => new LockReasonLink(
            tag: string.Empty,
            id: "x",
            detail: "y",
            navigationEntityType: null,
            navigationEntityId: null,
            because: "because",
            isTerminal: false);
        act.Should().Throw<ArgumentException>();
    }
}
