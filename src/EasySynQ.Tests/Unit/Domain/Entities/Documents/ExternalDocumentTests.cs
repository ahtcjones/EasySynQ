using AwesomeAssertions;

using EasySynQ.Domain.Entities.Documents;

using Xunit;

namespace EasySynQ.Tests.Unit.Domain.Entities.Documents;

public class ExternalDocumentTests
{
    [Fact]
    public void Constructor_PopulatesAllFields_NullableEffectiveDate()
    {
        var id = Guid.NewGuid();
        var effective = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var ext = new ExternalDocument(id, "ASTM", "ASTM A29", "2024", effective);

        ext.Id.Should().Be(id);
        ext.IssuingBody.Should().Be("ASTM");
        ext.Designation.Should().Be("ASTM A29");
        ext.CurrentRevisionLabel.Should().Be("2024");
        ext.CurrentEffectiveDateUtc.Should().Be(effective);
    }

    [Fact]
    public void Constructor_AcceptsNullEffectiveDate()
    {
        var ext = new ExternalDocument(
            Guid.NewGuid(), "AMS", "AMS 2750G", "G", currentEffectiveDateUtc: null);
        ext.CurrentEffectiveDateUtc.Should().BeNull();
    }

    [Fact]
    public void Constructor_RejectsEmptyId()
    {
        Action act = () =>
            new ExternalDocument(Guid.Empty, "ASTM", "ASTM A29", "2024", null);
        act.Should().Throw<ArgumentException>().WithMessage("*Id must not be Guid.Empty*");
    }

    [Fact]
    public void Constructor_RejectsBlankIssuingBody()
    {
        Action act = () =>
            new ExternalDocument(Guid.NewGuid(), " ", "ASTM A29", "2024", null);
        act.Should().Throw<ArgumentException>().WithMessage("*issuingBody*");
    }

    [Fact]
    public void Constructor_RejectsBlankDesignation()
    {
        Action act = () =>
            new ExternalDocument(Guid.NewGuid(), "ASTM", "", "2024", null);
        act.Should().Throw<ArgumentException>().WithMessage("*designation*");
    }

    [Fact]
    public void Constructor_RejectsBlankCurrentRevisionLabel()
    {
        Action act = () =>
            new ExternalDocument(Guid.NewGuid(), "ASTM", "ASTM A29", "\t", null);
        act.Should().Throw<ArgumentException>().WithMessage("*currentRevisionLabel*");
    }

    [Fact]
    public void Constructor_RejectsNonUtcEffectiveDate()
    {
        var local = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Local);
        Action act = () =>
            new ExternalDocument(Guid.NewGuid(), "ASTM", "ASTM A29", "2024", local);
        act.Should().Throw<ArgumentException>().WithMessage("*DateTimeKind.Utc*");
    }
}
