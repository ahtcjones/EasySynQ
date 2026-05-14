using AwesomeAssertions;

using EasySynQ.Domain.Entities.Identity;
using EasySynQ.Domain.ValueObjects;

using Xunit;

namespace EasySynQ.Tests.Unit.Domain.Entities;

public class RolePermissionTests
{
    private static readonly Guid SampleRoleId = new("a1111111-1111-1111-1111-111111111111");
    private static readonly Guid SamplePermissionId = new("b2222222-2222-2222-2222-222222222222");

    private static EffectiveDateRange ClosedPeriod(int fromYear, int toYear) =>
        new(
            new DateTime(fromYear, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(toYear, 12, 31, 0, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void IsInEffectAt_BeforePeriodStart_ReturnsFalse()
    {
        var rp = new RolePermission(Guid.NewGuid(), SampleRoleId, SamplePermissionId, ClosedPeriod(2024, 2024));
        var before = new DateTime(2023, 12, 1, 0, 0, 0, DateTimeKind.Utc);
        rp.IsInEffectAt(before).Should().BeFalse();
    }

    [Fact]
    public void IsInEffectAt_InsidePeriod_ReturnsTrue()
    {
        var rp = new RolePermission(Guid.NewGuid(), SampleRoleId, SamplePermissionId, ClosedPeriod(2024, 2024));
        var mid = new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        rp.IsInEffectAt(mid).Should().BeTrue();
    }

    [Fact]
    public void IsInEffectAt_AfterPeriodEnd_ReturnsFalse()
    {
        var rp = new RolePermission(Guid.NewGuid(), SampleRoleId, SamplePermissionId, ClosedPeriod(2024, 2024));
        var after = new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        rp.IsInEffectAt(after).Should().BeFalse();
    }

    [Fact]
    public void IsInEffectAt_OpenEndedPeriod_TreatedAsStillInEffect()
    {
        var period = new EffectiveDateRange(
            new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            effectiveToUtc: null);
        var rp = new RolePermission(Guid.NewGuid(), SampleRoleId, SamplePermissionId, period);
        rp.IsInEffectAt(DateTime.UtcNow).Should().BeTrue();
    }

    [Fact]
    public void Constructor_RejectsEmptyId()
    {
        var period = ClosedPeriod(2024, 2024);
        Action act = () => new RolePermission(Guid.Empty, SampleRoleId, SamplePermissionId, period);
        act.Should().Throw<ArgumentException>().WithMessage("*Id must not be Guid.Empty*");
    }

    [Fact]
    public void Constructor_RejectsEmptyRoleId()
    {
        var period = ClosedPeriod(2024, 2024);
        Action act = () => new RolePermission(Guid.NewGuid(), Guid.Empty, SamplePermissionId, period);
        act.Should().Throw<ArgumentException>().WithMessage("*RoleId*");
    }

    [Fact]
    public void Constructor_RejectsEmptyPermissionId()
    {
        var period = ClosedPeriod(2024, 2024);
        Action act = () => new RolePermission(Guid.NewGuid(), SampleRoleId, Guid.Empty, period);
        act.Should().Throw<ArgumentException>().WithMessage("*PermissionId*");
    }

    [Fact]
    public void Constructor_RejectsNullPeriod()
    {
        Action act = () => new RolePermission(Guid.NewGuid(), SampleRoleId, SamplePermissionId, null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
