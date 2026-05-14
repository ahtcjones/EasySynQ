using AwesomeAssertions;

using EasySynQ.Domain.Entities.Identity;

using Xunit;

namespace EasySynQ.Tests.Unit.Domain.Entities;

public class PermissionTests
{
    [Fact]
    public void Constructor_PopulatesAllFields()
    {
        var id = Guid.NewGuid();
        var p = new Permission(id, "Document.Approve", "Approve a document revision.", "Document");

        p.Id.Should().Be(id);
        p.Name.Should().Be("Document.Approve");
        p.Description.Should().Be("Approve a document revision.");
        p.Category.Should().Be("Document");
    }

    [Fact]
    public void Constructor_RejectsEmptyId()
    {
        Action act = () => new Permission(Guid.Empty, "X.Y", "desc", "cat");
        act.Should().Throw<ArgumentException>().WithMessage("*Id must not be Guid.Empty*");
    }

    [Fact]
    public void Constructor_RejectsBlankName()
    {
        Action act = () => new Permission(Guid.NewGuid(), "  ", "desc", "cat");
        act.Should().Throw<ArgumentException>().WithMessage("*name*");
    }

    [Fact]
    public void Constructor_RejectsBlankDescription()
    {
        Action act = () => new Permission(Guid.NewGuid(), "X.Y", "", "cat");
        act.Should().Throw<ArgumentException>().WithMessage("*description*");
    }

    [Fact]
    public void Constructor_RejectsBlankCategory()
    {
        Action act = () => new Permission(Guid.NewGuid(), "X.Y", "desc", "\t");
        act.Should().Throw<ArgumentException>().WithMessage("*category*");
    }
}
