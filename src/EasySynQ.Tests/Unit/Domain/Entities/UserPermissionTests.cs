using AwesomeAssertions;

using EasySynQ.Domain.Entities.Identity;
using EasySynQ.Domain.ValueObjects;

using Xunit;

namespace EasySynQ.Tests.Unit.Domain.Entities;

public class UserPermissionTests
{
    private static readonly Guid SampleUserId = new("a1111111-1111-1111-1111-111111111111");
    private static readonly Guid SamplePermissionId = new("b2222222-2222-2222-2222-222222222222");

    private static EffectiveDateRange ClosedPeriod(int fromYear, int toYear) =>
        new(
            new DateTime(fromYear, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(toYear, 12, 31, 0, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void IsInEffectAt_BeforePeriodStart_ReturnsFalse()
    {
        var up = new UserPermission(Guid.NewGuid(), SampleUserId, SamplePermissionId, ClosedPeriod(2024, 2024));
        var before = new DateTime(2023, 12, 1, 0, 0, 0, DateTimeKind.Utc);
        up.IsInEffectAt(before).Should().BeFalse();
    }

    [Fact]
    public void IsInEffectAt_InsidePeriod_ReturnsTrue()
    {
        var up = new UserPermission(Guid.NewGuid(), SampleUserId, SamplePermissionId, ClosedPeriod(2024, 2024));
        var mid = new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        up.IsInEffectAt(mid).Should().BeTrue();
    }

    [Fact]
    public void IsInEffectAt_AfterPeriodEnd_ReturnsFalse()
    {
        var up = new UserPermission(Guid.NewGuid(), SampleUserId, SamplePermissionId, ClosedPeriod(2024, 2024));
        var after = new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        up.IsInEffectAt(after).Should().BeFalse();
    }

    [Fact]
    public void IsInEffectAt_OpenEndedPeriod_TreatedAsStillInEffect()
    {
        var period = new EffectiveDateRange(
            new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            effectiveToUtc: null);
        var up = new UserPermission(Guid.NewGuid(), SampleUserId, SamplePermissionId, period);
        up.IsInEffectAt(DateTime.UtcNow).Should().BeTrue();
    }

    [Fact]
    public void Constructor_RejectsEmptyId()
    {
        var period = ClosedPeriod(2024, 2024);
        Action act = () => new UserPermission(Guid.Empty, SampleUserId, SamplePermissionId, period);
        act.Should().Throw<ArgumentException>().WithMessage("*Id must not be Guid.Empty*");
    }

    [Fact]
    public void Constructor_RejectsEmptyUserId()
    {
        var period = ClosedPeriod(2024, 2024);
        Action act = () => new UserPermission(Guid.NewGuid(), Guid.Empty, SamplePermissionId, period);
        act.Should().Throw<ArgumentException>().WithMessage("*UserId*");
    }

    [Fact]
    public void Constructor_RejectsEmptyPermissionId()
    {
        var period = ClosedPeriod(2024, 2024);
        Action act = () => new UserPermission(Guid.NewGuid(), SampleUserId, Guid.Empty, period);
        act.Should().Throw<ArgumentException>().WithMessage("*PermissionId*");
    }

    [Fact]
    public void Constructor_RejectsNullPeriod()
    {
        Action act = () => new UserPermission(Guid.NewGuid(), SampleUserId, SamplePermissionId, null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
