using AwesomeAssertions;

using EasySynQ.Domain.Entities.Documents;
using EasySynQ.Domain.Enums;

using Xunit;

namespace EasySynQ.Tests.Unit.Domain.Entities.Documents;

public class DocumentRevisionTests
{
    [Fact]
    public void Constructor_NewRevision_StartsInDraftWithNoSignatures()
    {
        var id = Guid.NewGuid();
        var docId = Guid.NewGuid();
        var authorId = Guid.NewGuid();

        var rev = new DocumentRevision(id, docId, "Rev A", authorId);

        rev.Id.Should().Be(id);
        rev.DocumentId.Should().Be(docId);
        rev.RevisionLabel.Should().Be("Rev A");
        rev.AuthorUserId.Should().Be(authorId);

        // Initial state per ADR 0008: lifecycle starts in Draft, no
        // signatures attached, no approval timestamp, no file. The
        // lifecycle service (C3) drives every subsequent transition.
        rev.Lifecycle.Should().Be(DocumentLifecycle.Draft);
        rev.EffectiveFromUtc.Should().BeNull();
        rev.ApprovedAtUtc.Should().BeNull();
        rev.VaultBlobId.Should().BeNull();
        rev.AuthorSignatureId.Should().BeNull();
        rev.LockedAtUtc.Should().BeNull();
    }

    [Fact]
    public void Constructor_RejectsEmptyId()
    {
        Action act = () =>
            new DocumentRevision(Guid.Empty, Guid.NewGuid(), "Rev A", Guid.NewGuid());
        act.Should().Throw<ArgumentException>().WithMessage("*Id must not be Guid.Empty*");
    }

    [Fact]
    public void Constructor_RejectsEmptyDocumentId()
    {
        Action act = () =>
            new DocumentRevision(Guid.NewGuid(), Guid.Empty, "Rev A", Guid.NewGuid());
        act.Should().Throw<ArgumentException>().WithMessage("*DocumentId*");
    }

    [Fact]
    public void Constructor_RejectsEmptyAuthorUserId()
    {
        Action act = () =>
            new DocumentRevision(Guid.NewGuid(), Guid.NewGuid(), "Rev A", Guid.Empty);
        act.Should().Throw<ArgumentException>().WithMessage("*AuthorUserId*");
    }

    [Fact]
    public void Constructor_RejectsBlankRevisionLabel()
    {
        Action act = () =>
            new DocumentRevision(Guid.NewGuid(), Guid.NewGuid(), "  ", Guid.NewGuid());
        act.Should().Throw<ArgumentException>().WithMessage("*revisionLabel*");
    }
}
