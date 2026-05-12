using AwesomeAssertions;

using EasySynQ.Domain.ValueObjects;

using Xunit;

namespace EasySynQ.Tests.Unit.Domain;

public class EffectiveDateRangeTests
{
    private static readonly DateTime PastFrom = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime PastTo = new(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Constructor_RejectsNonUtcFromKind()
    {
        var local = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Local);
        Action act = () => new EffectiveDateRange(local, null);
        act.Should().Throw<ArgumentException>().WithMessage("*DateTimeKind.Utc*");
    }

    [Fact]
    public void Constructor_RejectsNonUtcToKind()
    {
        var unspecifiedTo = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Unspecified);
        Action act = () => new EffectiveDateRange(PastFrom, unspecifiedTo);
        act.Should().Throw<ArgumentException>().WithMessage("*DateTimeKind.Utc*");
    }

    [Fact]
    public void Constructor_AcceptsFutureFrom()
    {
        // Per SPEC §3.7 (Rev 3.2): pre-scheduled configuration changes are
        // a supported workflow. A future EffectiveFromUtc is valid; the
        // as-of resolver excludes not-yet-active versions naturally.
        var futureFrom = DateTime.UtcNow.AddDays(7);
        var range = new EffectiveDateRange(futureFrom, null);
        range.EffectiveFromUtc.Should().Be(futureFrom);
    }

    [Fact]
    public void IsInEffectAt_BeforeFutureFromReturnsFalse()
    {
        // Confirms the as-of behavior cited by §3.7's pre-scheduled-change
        // workflow: a not-yet-active range reports IsInEffectAt(now) = false.
        var futureFrom = DateTime.UtcNow.AddDays(7);
        var range = new EffectiveDateRange(futureFrom, null);
        range.IsInEffectAt(DateTime.UtcNow).Should().BeFalse();
    }

    [Fact]
    public void Constructor_RejectsToBeforeFrom()
    {
        var earlierTo = PastFrom.AddDays(-1);
        Action act = () => new EffectiveDateRange(PastFrom, earlierTo);
        act.Should().Throw<ArgumentException>().WithMessage("*strictly after*");
    }

    [Fact]
    public void Constructor_RejectsToEqualToFrom()
    {
        Action act = () => new EffectiveDateRange(PastFrom, PastFrom);
        act.Should().Throw<ArgumentException>().WithMessage("*strictly after*");
    }

    [Fact]
    public void Constructor_AcceptsNullTo()
    {
        var range = new EffectiveDateRange(PastFrom, null);
        range.EffectiveFromUtc.Should().Be(PastFrom);
        range.EffectiveToUtc.Should().BeNull();
    }

    [Fact]
    public void IsInEffectAt_FromIsInclusive()
    {
        var range = new EffectiveDateRange(PastFrom, PastTo);
        range.IsInEffectAt(PastFrom).Should().BeTrue();
    }

    [Fact]
    public void IsInEffectAt_ToIsExclusive()
    {
        var range = new EffectiveDateRange(PastFrom, PastTo);
        range.IsInEffectAt(PastTo).Should().BeFalse();
    }

    [Fact]
    public void IsInEffectAt_BeforeFromReturnsFalse()
    {
        var range = new EffectiveDateRange(PastFrom, PastTo);
        range.IsInEffectAt(PastFrom.AddTicks(-1)).Should().BeFalse();
    }

    [Fact]
    public void IsInEffectAt_AfterToReturnsFalse()
    {
        var range = new EffectiveDateRange(PastFrom, PastTo);
        range.IsInEffectAt(PastTo.AddTicks(1)).Should().BeFalse();
    }

    [Fact]
    public void IsInEffectAt_InsideRangeReturnsTrue()
    {
        var range = new EffectiveDateRange(PastFrom, PastTo);
        var midpoint = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
        range.IsInEffectAt(midpoint).Should().BeTrue();
    }

    [Fact]
    public void IsInEffectAt_NullToTreatedAsStillInEffect()
    {
        var range = new EffectiveDateRange(PastFrom, null);
        range.IsInEffectAt(DateTime.UtcNow).Should().BeTrue();
    }

    [Fact]
    public void IsInEffectAt_RejectsNonUtcInstant()
    {
        var range = new EffectiveDateRange(PastFrom, PastTo);
        var local = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Local);
        Action act = () => range.IsInEffectAt(local);
        act.Should().Throw<ArgumentException>().WithMessage("*DateTimeKind.Utc*");
    }
}
