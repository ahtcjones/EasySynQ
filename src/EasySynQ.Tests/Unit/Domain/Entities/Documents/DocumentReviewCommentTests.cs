using AwesomeAssertions;

using EasySynQ.Domain.Entities.Documents;

using Xunit;

namespace EasySynQ.Tests.Unit.Domain.Entities.Documents;

public class DocumentReviewCommentTests
{
    private static readonly DateTime CreatedAt =
        new(2026, 5, 14, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Constructor_PopulatesAllFields()
    {
        var id = Guid.NewGuid();
        var revId = Guid.NewGuid();
        var authorId = Guid.NewGuid();

        var comment = new DocumentReviewComment(
            id, revId, authorId, "This SOP needs a clearer figure.", CreatedAt);

        comment.Id.Should().Be(id);
        comment.DocumentRevisionId.Should().Be(revId);
        comment.AuthorUserId.Should().Be(authorId);
        comment.BodyText.Should().Be("This SOP needs a clearer figure.");
        comment.CreatedAtUtc.Should().Be(CreatedAt);
    }

    [Fact]
    public void Constructor_RejectsEmptyId()
    {
        Action act = () => new DocumentReviewComment(
            Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), "body", CreatedAt);
        act.Should().Throw<ArgumentException>().WithMessage("*Id must not be Guid.Empty*");
    }

    [Fact]
    public void Constructor_RejectsEmptyDocumentRevisionId()
    {
        Action act = () => new DocumentReviewComment(
            Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), "body", CreatedAt);
        act.Should().Throw<ArgumentException>().WithMessage("*DocumentRevisionId*");
    }

    [Fact]
    public void Constructor_RejectsEmptyAuthorUserId()
    {
        Action act = () => new DocumentReviewComment(
            Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, "body", CreatedAt);
        act.Should().Throw<ArgumentException>().WithMessage("*AuthorUserId*");
    }

    [Fact]
    public void Constructor_RejectsBlankBodyText()
    {
        Action act = () => new DocumentReviewComment(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "   ", CreatedAt);
        act.Should().Throw<ArgumentException>().WithMessage("*bodyText*");
    }

    [Fact]
    public void Constructor_RejectsNonUtcCreatedAt()
    {
        var local = new DateTime(2026, 5, 14, 12, 0, 0, DateTimeKind.Local);
        Action act = () => new DocumentReviewComment(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "body", local);
        act.Should().Throw<ArgumentException>().WithMessage("*DateTimeKind.Utc*");
    }
}
