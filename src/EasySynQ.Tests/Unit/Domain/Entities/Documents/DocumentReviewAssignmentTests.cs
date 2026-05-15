using AwesomeAssertions;

using EasySynQ.Domain.Entities.Documents;
using EasySynQ.Domain.Enums;

using Xunit;

namespace EasySynQ.Tests.Unit.Domain.Entities.Documents;

public class DocumentReviewAssignmentTests
{
    private static readonly DateTime AssignedAt =
        new(2026, 5, 14, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Constructor_NewAssignment_StartsInPendingWithoutSignature()
    {
        var id = Guid.NewGuid();
        var revId = Guid.NewGuid();
        var reviewerId = Guid.NewGuid();
        var assignedById = Guid.NewGuid();

        var assignment = new DocumentReviewAssignment(
            id, revId, reviewerId, AssignedAt, assignedById);

        assignment.Id.Should().Be(id);
        assignment.DocumentRevisionId.Should().Be(revId);
        assignment.ReviewerUserId.Should().Be(reviewerId);
        assignment.AssignedAtUtc.Should().Be(AssignedAt);
        assignment.AssignedByUserId.Should().Be(assignedById);

        // Initial state per ADR 0008: pending with no signature.
        assignment.Status.Should().Be(DocumentReviewAssignmentStatus.Pending);
        assignment.SignedAtUtc.Should().BeNull();
        assignment.SignatureId.Should().BeNull();
    }

    [Fact]
    public void Constructor_RejectsEmptyId()
    {
        Action act = () => new DocumentReviewAssignment(
            Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), AssignedAt, Guid.NewGuid());
        act.Should().Throw<ArgumentException>().WithMessage("*Id must not be Guid.Empty*");
    }

    [Fact]
    public void Constructor_RejectsEmptyDocumentRevisionId()
    {
        Action act = () => new DocumentReviewAssignment(
            Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), AssignedAt, Guid.NewGuid());
        act.Should().Throw<ArgumentException>().WithMessage("*DocumentRevisionId*");
    }

    [Fact]
    public void Constructor_RejectsEmptyReviewerUserId()
    {
        Action act = () => new DocumentReviewAssignment(
            Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, AssignedAt, Guid.NewGuid());
        act.Should().Throw<ArgumentException>().WithMessage("*ReviewerUserId*");
    }

    [Fact]
    public void Constructor_RejectsEmptyAssignedByUserId()
    {
        Action act = () => new DocumentReviewAssignment(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), AssignedAt, Guid.Empty);
        act.Should().Throw<ArgumentException>().WithMessage("*AssignedByUserId*");
    }

    [Fact]
    public void Constructor_RejectsNonUtcAssignedAt()
    {
        var local = new DateTime(2026, 5, 14, 12, 0, 0, DateTimeKind.Local);
        Action act = () => new DocumentReviewAssignment(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), local, Guid.NewGuid());
        act.Should().Throw<ArgumentException>().WithMessage("*DateTimeKind.Utc*");
    }
}
