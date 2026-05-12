using AwesomeAssertions;

using EasySynQ.Domain.Entities.Audit;

using Xunit;

namespace EasySynQ.Tests.Unit.Domain.Entities;

public class SignatureTests
{
    private const string ValidHash =
        "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";

    private static readonly DateTime SignedAt = new(2026, 5, 11, 12, 0, 0, DateTimeKind.Utc);

    private static Signature MakeValid(Guid? id = null) => new(
        id ?? Guid.NewGuid(),
        SignedAt,
        "QualityManager",
        "Job",
        "J-2026-0847",
        ValidHash);

    [Fact]
    public void Constructor_AcceptsValidInputs()
    {
        var sig = MakeValid();
        sig.UtcTimestamp.Should().Be(SignedAt);
        sig.RoleAtTimeOfSign.Should().Be("QualityManager");
        sig.SignedEntityType.Should().Be("Job");
        sig.SignedEntityId.Should().Be("J-2026-0847");
        sig.PayloadHash.Should().Be(ValidHash);
    }

    [Fact]
    public void Signature_IsAnEntityNotARecord_TwoInstancesWithSameDataAreNotEqual()
    {
        // Signature is an entity (class), not a value object (record).
        // Two distinct instances with identical field values are
        // reference-unequal. If we later want Id-based equality
        // semantics, that's an explicit override.
        var id = Guid.NewGuid();
        var sig1 = new Signature(id, SignedAt, "QualityManager", "Job", "J-1", ValidHash);
        var sig2 = new Signature(id, SignedAt, "QualityManager", "Job", "J-1", ValidHash);
        sig1.Equals(sig2).Should().BeFalse();
    }

    [Fact]
    public void Constructor_RejectsEmptyId()
    {
        Action act = () => new Signature(
            Guid.Empty, SignedAt,
            "QualityManager", "Job", "J-2026-0847", ValidHash);
        act.Should().Throw<ArgumentException>().WithMessage("*Id must not be Guid.Empty*");
    }

    [Fact]
    public void Constructor_RejectsNonUtcTimestamp()
    {
        var local = new DateTime(2026, 5, 11, 12, 0, 0, DateTimeKind.Local);
        Action act = () => new Signature(
            Guid.NewGuid(), local,
            "QualityManager", "Job", "J-2026-0847", ValidHash);
        act.Should().Throw<ArgumentException>().WithMessage("*DateTimeKind.Utc*");
    }

    [Fact]
    public void Constructor_RejectsUppercaseHash()
    {
        const string upperHash =
            "ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789";
        Action act = () => new Signature(
            Guid.NewGuid(), SignedAt,
            "QualityManager", "Job", "J-2026-0847", upperHash);
        act.Should().Throw<ArgumentException>().WithMessage("*64 lowercase hexadecimal*");
    }

    [Fact]
    public void Constructor_RejectsShortHash()
    {
        Action act = () => new Signature(
            Guid.NewGuid(), SignedAt,
            "QualityManager", "Job", "J-2026-0847", new string('a', 63));
        act.Should().Throw<ArgumentException>().WithMessage("*64 lowercase hexadecimal*");
    }

    [Fact]
    public void Constructor_RejectsEmptyRole()
    {
        Action act = () => new Signature(
            Guid.NewGuid(), SignedAt,
            string.Empty, "Job", "J-2026-0847", ValidHash);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_RejectsEmptySignedEntityType()
    {
        Action act = () => new Signature(
            Guid.NewGuid(), SignedAt,
            "QualityManager", string.Empty, "J-2026-0847", ValidHash);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_RejectsEmptySignedEntityId()
    {
        Action act = () => new Signature(
            Guid.NewGuid(), SignedAt,
            "QualityManager", "Job", string.Empty, ValidHash);
        act.Should().Throw<ArgumentException>();
    }
}
