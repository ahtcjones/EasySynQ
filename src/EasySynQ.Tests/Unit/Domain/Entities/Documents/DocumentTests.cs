using AwesomeAssertions;

using EasySynQ.Domain.Entities.Documents;

using Xunit;

namespace EasySynQ.Tests.Unit.Domain.Entities.Documents;

public class DocumentTests
{
    [Fact]
    public void Constructor_PopulatesIdNumberTitle_LeavesRetirementFieldsNull()
    {
        var id = Guid.NewGuid();
        var doc = new Document(id, "SOP-Q-001", "Quality manual entry SOP");

        doc.Id.Should().Be(id);
        doc.Number.Should().Be("SOP-Q-001");
        doc.Title.Should().Be("Quality manual entry SOP");
        doc.RetiredAtUtc.Should().BeNull();
        doc.RetiredByUserId.Should().BeNull();
        doc.RetirementSignatureId.Should().BeNull();
    }

    [Fact]
    public void Constructor_RejectsEmptyId()
    {
        Action act = () => new Document(Guid.Empty, "SOP-Q-001", "Title");
        act.Should().Throw<ArgumentException>().WithMessage("*Id must not be Guid.Empty*");
    }

    [Fact]
    public void Constructor_RejectsBlankNumber()
    {
        Action act = () => new Document(Guid.NewGuid(), "  ", "Title");
        act.Should().Throw<ArgumentException>().WithMessage("*number*");
    }

    [Fact]
    public void Constructor_RejectsBlankTitle()
    {
        Action act = () => new Document(Guid.NewGuid(), "SOP-Q-001", "");
        act.Should().Throw<ArgumentException>().WithMessage("*title*");
    }
}
