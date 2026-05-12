using AwesomeAssertions;

using EasySynQ.Domain.Entities.Audit;
using EasySynQ.Domain.Enums;

using Xunit;

namespace EasySynQ.Tests.Unit.Domain.Entities;

public class AuditLogEntryTests
{
    private static readonly DateTime Timestamp =
        new(2026, 5, 11, 12, 0, 0, DateTimeKind.Utc);

    private const string SampleBefore = "{\"x\":1}";
    private const string SampleAfter = "{\"x\":2}";

    [Fact]
    public void Constructor_Insert_RejectsNonNullBefore()
    {
        Action act = () => new AuditLogEntry(
            Guid.NewGuid(), Timestamp, userId: null,
            "User", "user-1", AuditAction.Insert,
            before: SampleBefore,
            after: SampleAfter,
            correlationId: Guid.NewGuid());
        act.Should().Throw<ArgumentException>().WithMessage("*Insert*Before*null*");
    }

    [Fact]
    public void Constructor_Insert_RejectsNullAfter()
    {
        Action act = () => new AuditLogEntry(
            Guid.NewGuid(), Timestamp, userId: null,
            "User", "user-1", AuditAction.Insert,
            before: null,
            after: null,
            correlationId: Guid.NewGuid());
        act.Should().Throw<ArgumentException>().WithMessage("*Insert*After*");
    }

    [Fact]
    public void Constructor_HardDelete_RejectsNonNullAfter()
    {
        Action act = () => new AuditLogEntry(
            Guid.NewGuid(), Timestamp, userId: null,
            "User", "user-1", AuditAction.HardDelete,
            before: SampleBefore,
            after: SampleAfter,
            correlationId: Guid.NewGuid());
        act.Should().Throw<ArgumentException>().WithMessage("*HardDelete*After*null*");
    }

    [Fact]
    public void Constructor_HardDelete_RejectsNullBefore()
    {
        Action act = () => new AuditLogEntry(
            Guid.NewGuid(), Timestamp, userId: null,
            "User", "user-1", AuditAction.HardDelete,
            before: null,
            after: null,
            correlationId: Guid.NewGuid());
        act.Should().Throw<ArgumentException>().WithMessage("*HardDelete*Before*");
    }

    [Fact]
    public void Constructor_Update_RejectsNullBefore()
    {
        Action act = () => new AuditLogEntry(
            Guid.NewGuid(), Timestamp, userId: null,
            "User", "user-1", AuditAction.Update,
            before: null,
            after: SampleAfter,
            correlationId: Guid.NewGuid());
        act.Should().Throw<ArgumentException>().WithMessage("*Update*Before*");
    }

    [Fact]
    public void Constructor_Update_RejectsNullAfter()
    {
        Action act = () => new AuditLogEntry(
            Guid.NewGuid(), Timestamp, userId: null,
            "User", "user-1", AuditAction.Update,
            before: SampleBefore,
            after: null,
            correlationId: Guid.NewGuid());
        act.Should().Throw<ArgumentException>().WithMessage("*Update*After*");
    }

    [Fact]
    public void Constructor_Delete_RejectsNullBefore()
    {
        Action act = () => new AuditLogEntry(
            Guid.NewGuid(), Timestamp, userId: null,
            "User", "user-1", AuditAction.Delete,
            before: null,
            after: SampleAfter,
            correlationId: Guid.NewGuid());
        act.Should().Throw<ArgumentException>().WithMessage("*Before*");
    }

    [Fact]
    public void Constructor_AcceptsValidInsert()
    {
        var entry = new AuditLogEntry(
            Guid.NewGuid(), Timestamp, userId: null,
            "User", "user-1", AuditAction.Insert,
            before: null,
            after: SampleAfter,
            correlationId: Guid.NewGuid());
        entry.Action.Should().Be(AuditAction.Insert);
        entry.Before.Should().BeNull();
        entry.After.Should().Be(SampleAfter);
    }

    [Fact]
    public void Constructor_RoundTripsCorrelationId()
    {
        var corrId = Guid.NewGuid();
        var entry = new AuditLogEntry(
            Guid.NewGuid(), Timestamp, userId: null,
            "User", "user-1", AuditAction.Insert,
            before: null,
            after: SampleAfter,
            correlationId: corrId);
        entry.CorrelationId.Should().Be(corrId);
    }

    [Fact]
    public void AuditLogEntry_IsAnEntityNotARecord_TwoInstancesWithSameDataAreNotEqual()
    {
        // AuditLogEntry is a class (entity) without an Equals override.
        // Default reference equality applies — two distinct instances
        // with identical field values are NOT equal.
        var id = Guid.NewGuid();
        var corrId = Guid.NewGuid();
        var a = new AuditLogEntry(id, Timestamp, null, "User", "user-1",
            AuditAction.Insert, null, SampleAfter, corrId);
        var b = new AuditLogEntry(id, Timestamp, null, "User", "user-1",
            AuditAction.Insert, null, SampleAfter, corrId);
        a.Equals(b).Should().BeFalse();
    }
}
