using AwesomeAssertions;

using EasySynQ.Domain.Entities.Documents;

using Xunit;

namespace EasySynQ.Tests.Unit.Domain.Entities.Documents;

public class DocumentLinkTests
{
    [Fact]
    public void Constructor_PopulatesIds_FlagDefaultsFalse()
    {
        var id = Guid.NewGuid();
        var revId = Guid.NewGuid();
        var extId = Guid.NewGuid();

        var link = new DocumentLink(id, revId, extId);

        link.Id.Should().Be(id);
        link.DocumentRevisionId.Should().Be(revId);
        link.ExternalDocumentId.Should().Be(extId);
        link.CompatibilityReviewRequiredFlag.Should().BeFalse();
    }

    [Fact]
    public void Constructor_RejectsEmptyId()
    {
        Action act = () => new DocumentLink(Guid.Empty, Guid.NewGuid(), Guid.NewGuid());
        act.Should().Throw<ArgumentException>().WithMessage("*Id must not be Guid.Empty*");
    }

    [Fact]
    public void Constructor_RejectsEmptyDocumentRevisionId()
    {
        Action act = () => new DocumentLink(Guid.NewGuid(), Guid.Empty, Guid.NewGuid());
        act.Should().Throw<ArgumentException>().WithMessage("*DocumentRevisionId*");
    }

    [Fact]
    public void Constructor_RejectsEmptyExternalDocumentId()
    {
        Action act = () => new DocumentLink(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty);
        act.Should().Throw<ArgumentException>().WithMessage("*ExternalDocumentId*");
    }
}
