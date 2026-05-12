using AwesomeAssertions;

using EasySynQ.Domain.Common;

using Xunit;

namespace EasySynQ.Tests.Unit.Domain;

public class HashFormatTests
{
    private const string ValidLowercase64 =
        "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";

    [Fact]
    public void IsValidSha256Hex_AcceptsValidLowercase64()
    {
        HashFormat.IsValidSha256Hex(ValidLowercase64).Should().BeTrue();
    }

    [Fact]
    public void IsValidSha256Hex_RejectsUppercase()
    {
        const string upper =
            "ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789";
        HashFormat.IsValidSha256Hex(upper).Should().BeFalse();
    }

    [Fact]
    public void IsValidSha256Hex_RejectsMixedCase()
    {
        const string mixed =
            "AbCdEf0123456789abcdef0123456789abcdef0123456789abcdef0123456789";
        HashFormat.IsValidSha256Hex(mixed).Should().BeFalse();
    }

    [Fact]
    public void IsValidSha256Hex_RejectsNonHexCharacter()
    {
        const string nonHex =
            "g23def0123456789abcdef0123456789abcdef0123456789abcdef0123456789";
        HashFormat.IsValidSha256Hex(nonHex).Should().BeFalse();
    }

    [Fact]
    public void IsValidSha256Hex_RejectsShorterThan64Chars()
    {
        HashFormat.IsValidSha256Hex(new string('a', 63)).Should().BeFalse();
    }

    [Fact]
    public void IsValidSha256Hex_RejectsLongerThan64Chars()
    {
        HashFormat.IsValidSha256Hex(new string('a', 65)).Should().BeFalse();
    }

    [Fact]
    public void IsValidSha256Hex_RejectsNull()
    {
        HashFormat.IsValidSha256Hex(null).Should().BeFalse();
    }

    [Fact]
    public void IsValidSha256Hex_RejectsEmptyString()
    {
        HashFormat.IsValidSha256Hex(string.Empty).Should().BeFalse();
    }
}
