using AwesomeAssertions;

using EasySynQ.Domain.Entities.Documents;
using EasySynQ.Domain.Enums;

using Xunit;

namespace EasySynQ.Tests.Unit.Domain.Entities.Documents;

/// <summary>
/// Unit tests for the lifecycle-mutating methods on
/// <see cref="DocumentReviewAssignment"/> (ADR 0008 C3): RecordSignature
/// + Discard.
/// </summary>
public class DocumentReviewAssignmentLifecycleTests
{
    private static DateTime UtcNow(int offsetSeconds = 0) =>
        new DateTime(2026, 5, 15, 12, 0, 0, DateTimeKind.Utc).AddSeconds(offsetSeconds);

    private static DocumentReviewAssignment NewPendingAssignment() =>
        new(
            id: Guid.NewGuid(),
            documentRevisionId: Guid.NewGuid(),
            reviewerUserId: Guid.NewGuid(),
            assignedAtUtc: UtcNow(),
            assignedByUserId: Guid.NewGuid());

    // ─── RecordSignature ────────────────────────────────────────────

    [Fact]
    public void RecordSignature_FromPending_TransitionsAndStampsFields()
    {
        var assignment = NewPendingAssignment();
        var sigId = Guid.NewGuid();
        var signedAt = UtcNow(60);

        assignment.RecordSignature(sigId, signedAt);

        assignment.Status.Should().Be(DocumentReviewAssignmentStatus.Signed);
        assignment.SignatureId.Should().Be(sigId);
        assignment.SignedAtUtc.Should().Be(signedAt);
    }

    [Fact]
    public void RecordSignature_OnAlreadySigned_Throws()
    {
        var assignment = NewPendingAssignment();
        assignment.RecordSignature(Guid.NewGuid(), UtcNow(60));

        Action act = () => assignment.RecordSignature(Guid.NewGuid(), UtcNow(120));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Cannot record signature*");
    }

    [Fact]
    public void RecordSignature_OnDiscarded_Throws()
    {
        var assignment = NewPendingAssignment();
        assignment.Discard();

        Action act = () => assignment.RecordSignature(Guid.NewGuid(), UtcNow(60));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Cannot record signature*");
    }

    [Fact]
    public void RecordSignature_RejectsEmptySignatureId()
    {
        var assignment = NewPendingAssignment();

        Action act = () => assignment.RecordSignature(Guid.Empty, UtcNow(60));

        act.Should().Throw<ArgumentException>()
            .WithMessage("*SignatureId must not be Guid.Empty*");
    }

    [Fact]
    public void RecordSignature_RejectsNonUtcInstant()
    {
        var assignment = NewPendingAssignment();
        var local = new DateTime(2026, 5, 15, 13, 0, 0, DateTimeKind.Local);

        Action act = () => assignment.RecordSignature(Guid.NewGuid(), local);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*SignedAtUtc must have DateTimeKind.Utc*");
    }

    // ─── Discard ────────────────────────────────────────────────────

    [Fact]
    public void Discard_FromPending_Transitions()
    {
        var assignment = NewPendingAssignment();

        assignment.Discard();

        assignment.Status.Should().Be(DocumentReviewAssignmentStatus.Discarded);
    }

    [Fact]
    public void Discard_FromSigned_TransitionsAndPreservesSignatureFields()
    {
        var assignment = NewPendingAssignment();
        var sigId = Guid.NewGuid();
        var signedAt = UtcNow(60);
        assignment.RecordSignature(sigId, signedAt);

        assignment.Discard();

        assignment.Status.Should().Be(DocumentReviewAssignmentStatus.Discarded);
        // The Signature row reference is preserved per ADR 0008
        // §"Signatures reset" — discarded assignments still point at
        // their signature for the audit trail.
        assignment.SignatureId.Should().Be(sigId);
        assignment.SignedAtUtc.Should().Be(signedAt);
    }

    [Fact]
    public void Discard_OnAlreadyDiscarded_Throws()
    {
        // Per ADR 0008 C3 plan §G Q4 — idempotent no-ops mask bugs.
        var assignment = NewPendingAssignment();
        assignment.Discard();

        Action act = () => assignment.Discard();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*already in 'Discarded'*");
    }
}
