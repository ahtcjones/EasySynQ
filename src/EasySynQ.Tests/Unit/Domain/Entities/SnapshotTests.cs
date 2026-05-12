using AwesomeAssertions;

using EasySynQ.Domain.Entities.Snapshots;
using EasySynQ.Domain.Enums;

using Xunit;

namespace EasySynQ.Tests.Unit.Domain.Entities;

public class SnapshotTests
{
    private const string ValidHash =
        "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";

    private static readonly DateTime CreatedAt =
        new(2026, 5, 11, 0, 0, 0, DateTimeKind.Utc);

    // CA1861: extracted to a static readonly field so the array isn't
    // re-allocated on every test invocation.
    private static readonly string[] ExpectedTierNames = ["Daily", "Weekly", "Monthly"];

    private static Snapshot MakeValid() => new(
        Guid.NewGuid(),
        SnapshotTier.Daily,
        CreatedAt,
        "daily/2026-05-11.zip",
        ValidHash,
        1024,
        512,
        256);

    [Fact]
    public void SnapshotTier_EnumExposesExpectedMembers()
    {
        Enum.GetNames<SnapshotTier>().Should().BeEquivalentTo(ExpectedTierNames);
    }

    [Fact]
    public void Constructor_StartsUnverified()
    {
        var snap = MakeValid();
        snap.IntegrityVerified.Should().BeFalse();
        snap.IntegrityVerifiedUtc.Should().BeNull();
    }

    [Fact]
    public void MarkIntegrityVerified_SetsFlagAndTimestamp()
    {
        var snap = MakeValid();
        var verifiedAt = new DateTime(2026, 5, 12, 0, 0, 0, DateTimeKind.Utc);
        snap.MarkIntegrityVerified(verifiedAt);
        snap.IntegrityVerified.Should().BeTrue();
        snap.IntegrityVerifiedUtc.Should().Be(verifiedAt);
    }

    [Fact]
    public void MarkIntegrityVerified_IsRepeatable_UpdatesTimestamp()
    {
        var snap = MakeValid();
        var first = new DateTime(2026, 5, 12, 0, 0, 0, DateTimeKind.Utc);
        var second = new DateTime(2026, 5, 13, 0, 0, 0, DateTimeKind.Utc);
        snap.MarkIntegrityVerified(first);
        snap.MarkIntegrityVerified(second);
        snap.IntegrityVerified.Should().BeTrue();
        snap.IntegrityVerifiedUtc.Should().Be(second);
    }

    [Fact]
    public void MarkIntegrityVerified_RejectsNonUtc()
    {
        var snap = MakeValid();
        var local = new DateTime(2026, 5, 12, 0, 0, 0, DateTimeKind.Local);
        Action act = () => snap.MarkIntegrityVerified(local);
        act.Should().Throw<ArgumentException>().WithMessage("*DateTimeKind.Utc*");
    }

    [Fact]
    public void Constructor_RejectsInvalidHashFormat()
    {
        const string upperHash =
            "ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789";
        Action act = () => new Snapshot(
            Guid.NewGuid(), SnapshotTier.Daily, CreatedAt,
            "daily/2026-05-11.zip", upperHash, 1024, 512, 256);
        act.Should().Throw<ArgumentException>().WithMessage("*64 lowercase hexadecimal*");
    }

    [Fact]
    public void Constructor_RejectsNegativeByteSize()
    {
        Action act = () => new Snapshot(
            Guid.NewGuid(), SnapshotTier.Daily, CreatedAt,
            "daily/2026-05-11.zip", ValidHash, -1, 512, 256);
        act.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*non-negative*");
    }

    [Fact]
    public void Constructor_RejectsEmptyFilePath()
    {
        Action act = () => new Snapshot(
            Guid.NewGuid(), SnapshotTier.Daily, CreatedAt,
            string.Empty, ValidHash, 1024, 512, 256);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_RejectsNonUtcCreatedAt()
    {
        var local = new DateTime(2026, 5, 11, 0, 0, 0, DateTimeKind.Local);
        Action act = () => new Snapshot(
            Guid.NewGuid(), SnapshotTier.Daily, local,
            "daily/2026-05-11.zip", ValidHash, 1024, 512, 256);
        act.Should().Throw<ArgumentException>().WithMessage("*DateTimeKind.Utc*");
    }
}
