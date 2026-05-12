using AwesomeAssertions;

using EasySynQ.Domain.ValueObjects;

using Xunit;

namespace EasySynQ.Tests.Unit.Domain;

public class SignaturePayloadTests
{
    private const string ValidHash =
        "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";

    private static readonly DateTime Timestamp =
        new(2026, 5, 11, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Equality_SameValuesAreEqual()
    {
        var a = new SignaturePayload("user1", Timestamp, "QualityManager", ValidHash);
        var b = new SignaturePayload("user1", Timestamp, "QualityManager", ValidHash);

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Equality_DifferentUserIdsAreNotEqual()
    {
        var a = new SignaturePayload("user1", Timestamp, "QualityManager", ValidHash);
        var b = new SignaturePayload("user2", Timestamp, "QualityManager", ValidHash);

        a.Should().NotBe(b);
    }

    [Fact]
    public void Equality_DifferentRolesAreNotEqual()
    {
        var a = new SignaturePayload("user1", Timestamp, "QualityManager", ValidHash);
        var b = new SignaturePayload("user1", Timestamp, "LabTech", ValidHash);

        a.Should().NotBe(b);
    }

    [Fact]
    public void Equality_DifferentHashesAreNotEqual()
    {
        const string hashB =
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        var a = new SignaturePayload("user1", Timestamp, "QualityManager", ValidHash);
        var b = new SignaturePayload("user1", Timestamp, "QualityManager", hashB);

        a.Should().NotBe(b);
    }

    [Fact]
    public void Constructor_AcceptsValidInputs()
    {
        var p = new SignaturePayload("user1", Timestamp, "QualityManager", ValidHash);

        p.UserId.Should().Be("user1");
        p.UtcTimestamp.Should().Be(Timestamp);
        p.RoleAtTimeOfSign.Should().Be("QualityManager");
        p.PayloadHash.Should().Be(ValidHash);
    }

    [Fact]
    public void Constructor_RejectsHashShorterThan64Chars()
    {
        var shortHash = new string('a', 63);
        Action act = () => new SignaturePayload("user1", Timestamp, "QualityManager", shortHash);
        act.Should().Throw<ArgumentException>().WithMessage("*64 lowercase hexadecimal*");
    }

    [Fact]
    public void Constructor_RejectsHashLongerThan64Chars()
    {
        var longHash = new string('a', 65);
        Action act = () => new SignaturePayload("user1", Timestamp, "QualityManager", longHash);
        act.Should().Throw<ArgumentException>().WithMessage("*64 lowercase hexadecimal*");
    }

    [Fact]
    public void Constructor_RejectsUppercaseHex()
    {
        const string upperHash =
            "ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789";
        Action act = () => new SignaturePayload("user1", Timestamp, "QualityManager", upperHash);
        act.Should().Throw<ArgumentException>().WithMessage("*64 lowercase hexadecimal*");
    }

    [Fact]
    public void Constructor_RejectsNonHexCharacters()
    {
        const string nonHex =
            "ghijkl0123456789abcdef0123456789abcdef0123456789abcdef0123456789";
        Action act = () => new SignaturePayload("user1", Timestamp, "QualityManager", nonHex);
        act.Should().Throw<ArgumentException>().WithMessage("*64 lowercase hexadecimal*");
    }

    [Fact]
    public void Constructor_RejectsNonUtcTimestamp()
    {
        var local = new DateTime(2026, 5, 11, 12, 0, 0, DateTimeKind.Local);
        Action act = () => new SignaturePayload("user1", local, "QualityManager", ValidHash);
        act.Should().Throw<ArgumentException>().WithMessage("*DateTimeKind.Utc*");
    }

    [Fact]
    public void Constructor_RejectsEmptyUserId()
    {
        Action act = () => new SignaturePayload(string.Empty, Timestamp, "QualityManager", ValidHash);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_RejectsEmptyRole()
    {
        Action act = () => new SignaturePayload("user1", Timestamp, string.Empty, ValidHash);
        act.Should().Throw<ArgumentException>();
    }
}
