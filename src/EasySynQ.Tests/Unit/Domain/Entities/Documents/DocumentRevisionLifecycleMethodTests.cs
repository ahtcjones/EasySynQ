using AwesomeAssertions;

using EasySynQ.Domain.Entities.Documents;
using EasySynQ.Domain.Enums;

using Xunit;

namespace EasySynQ.Tests.Unit.Domain.Entities.Documents;

/// <summary>
/// Unit tests for the lifecycle-mutating methods on
/// <see cref="DocumentRevision"/> (ADR 0008 C3): Submit / ReturnToDraft
/// / Approve / Supersede / Archive / SetEffectiveFromUtc.
/// </summary>
public class DocumentRevisionLifecycleMethodTests
{
    private static DocumentRevision NewDraftRevision() =>
        new(Guid.NewGuid(), Guid.NewGuid(), "Rev A", Guid.NewGuid());

    private static DateTime UtcNow(int offsetSeconds = 0) =>
        new DateTime(2026, 5, 15, 12, 0, 0, DateTimeKind.Utc).AddSeconds(offsetSeconds);

    // ─── SetEffectiveFromUtc ────────────────────────────────────────

    [Fact]
    public void SetEffectiveFromUtc_FromDraft_AcceptsValue()
    {
        var rev = NewDraftRevision();
        var effective = UtcNow(60);

        rev.SetEffectiveFromUtc(effective);

        rev.EffectiveFromUtc.Should().Be(effective);
    }

    [Fact]
    public void SetEffectiveFromUtc_FromDraft_AcceptsNull()
    {
        var rev = NewDraftRevision();
        rev.SetEffectiveFromUtc(UtcNow(60));

        rev.SetEffectiveFromUtc(null);

        rev.EffectiveFromUtc.Should().BeNull();
    }

    [Fact]
    public void SetEffectiveFromUtc_FromInReview_Throws()
    {
        var rev = NewDraftRevision();
        rev.Submit(Guid.NewGuid(), UtcNow());

        Action act = () => rev.SetEffectiveFromUtc(UtcNow(120));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Cannot set EffectiveFromUtc*");
    }

    [Fact]
    public void SetEffectiveFromUtc_RejectsNonUtcValue()
    {
        var rev = NewDraftRevision();
        var local = new DateTime(2026, 5, 15, 12, 0, 0, DateTimeKind.Local);

        Action act = () => rev.SetEffectiveFromUtc(local);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*EffectiveFromUtc must have DateTimeKind.Utc*");
    }

    // ─── Submit ─────────────────────────────────────────────────────

    [Fact]
    public void Submit_FromDraft_TransitionsAndStampsAuthorSignatureAndLockedAt()
    {
        var rev = NewDraftRevision();
        var sigId = Guid.NewGuid();
        var lockedAt = UtcNow();

        rev.Submit(sigId, lockedAt);

        rev.Lifecycle.Should().Be(DocumentLifecycle.InReview);
        rev.AuthorSignatureId.Should().Be(sigId);
        rev.LockedAtUtc.Should().Be(lockedAt);
    }

    [Fact]
    public void Submit_FromInReview_Throws()
    {
        var rev = NewDraftRevision();
        rev.Submit(Guid.NewGuid(), UtcNow());

        Action act = () => rev.Submit(Guid.NewGuid(), UtcNow(60));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Cannot submit revision*");
    }

    [Fact]
    public void Submit_RejectsEmptySignatureId()
    {
        var rev = NewDraftRevision();

        Action act = () => rev.Submit(Guid.Empty, UtcNow());

        act.Should().Throw<ArgumentException>()
            .WithMessage("*AuthorSignatureId*");
    }

    [Fact]
    public void Submit_RejectsNonUtcLockedAt()
    {
        var rev = NewDraftRevision();
        var local = new DateTime(2026, 5, 15, 12, 0, 0, DateTimeKind.Local);

        Action act = () => rev.Submit(Guid.NewGuid(), local);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*LockedAtUtc must have DateTimeKind.Utc*");
    }

    // ─── ReturnToDraft ──────────────────────────────────────────────

    [Fact]
    public void ReturnToDraft_FromInReview_TransitionsAndClearsAuthorSignatureAndPreservesLockedAt()
    {
        var rev = NewDraftRevision();
        var lockedAt = UtcNow();
        rev.Submit(Guid.NewGuid(), lockedAt);

        rev.ReturnToDraft("needs more detail in section 3");

        rev.Lifecycle.Should().Be(DocumentLifecycle.Draft);
        rev.AuthorSignatureId.Should().BeNull();
        // Per SignableEntity contract — LockedAtUtc is one-way and
        // remains stamped even after a return-to-draft.
        rev.LockedAtUtc.Should().Be(lockedAt);
        rev.LastReturnToDraftReason.Should().Be("needs more detail in section 3");
    }

    [Fact]
    public void ReturnToDraft_FromDraft_Throws()
    {
        var rev = NewDraftRevision();

        Action act = () => rev.ReturnToDraft("reason");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Cannot return revision*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ReturnToDraft_NullOrWhitespaceReason_Throws(string? reason)
    {
        var rev = NewDraftRevision();
        rev.Submit(Guid.NewGuid(), UtcNow());

        Action act = () => rev.ReturnToDraft(reason!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Submit_AfterReturnToDraft_DoesNotResetLockedAtUtcAndClearsReason()
    {
        var rev = NewDraftRevision();
        var firstLock = UtcNow();
        rev.Submit(Guid.NewGuid(), firstLock);
        rev.ReturnToDraft("first review failed");

        rev.LastReturnToDraftReason.Should().Be("first review failed");

        var laterLock = UtcNow(120);
        rev.Submit(Guid.NewGuid(), laterLock);

        rev.LockedAtUtc.Should().Be(firstLock);
        // Re-submission clears the live LastReturnToDraftReason —
        // the prior reason survives in the audit log only.
        rev.LastReturnToDraftReason.Should().BeNull();
    }

    // ─── Approve ────────────────────────────────────────────────────

    [Fact]
    public void Approve_FromInReview_TransitionsAndStampsApprovedAt()
    {
        var rev = NewDraftRevision();
        rev.Submit(Guid.NewGuid(), UtcNow());
        var approvedAt = UtcNow(300);

        rev.Approve(approvedAt);

        rev.Lifecycle.Should().Be(DocumentLifecycle.Approved);
        rev.ApprovedAtUtc.Should().Be(approvedAt);
    }

    [Fact]
    public void Approve_FromDraft_Throws()
    {
        var rev = NewDraftRevision();

        Action act = () => rev.Approve(UtcNow());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Cannot approve revision*");
    }

    [Fact]
    public void Approve_RejectsNonUtcInstant()
    {
        var rev = NewDraftRevision();
        rev.Submit(Guid.NewGuid(), UtcNow());
        var local = new DateTime(2026, 5, 15, 13, 0, 0, DateTimeKind.Local);

        Action act = () => rev.Approve(local);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*ApprovedAtUtc must have DateTimeKind.Utc*");
    }

    // ─── Supersede ──────────────────────────────────────────────────

    [Fact]
    public void Supersede_FromApproved_Transitions()
    {
        var rev = NewDraftRevision();
        rev.Submit(Guid.NewGuid(), UtcNow());
        rev.Approve(UtcNow(60));

        rev.Supersede();

        rev.Lifecycle.Should().Be(DocumentLifecycle.Superseded);
    }

    [Fact]
    public void Supersede_FromDraft_Throws()
    {
        var rev = NewDraftRevision();

        Action act = () => rev.Supersede();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Cannot supersede revision*");
    }

    [Fact]
    public void Supersede_FromInReview_Throws()
    {
        var rev = NewDraftRevision();
        rev.Submit(Guid.NewGuid(), UtcNow());

        Action act = () => rev.Supersede();

        act.Should().Throw<InvalidOperationException>();
    }

    // ─── Archive ────────────────────────────────────────────────────

    [Fact]
    public void Archive_FromApproved_Transitions()
    {
        var rev = NewDraftRevision();
        rev.Submit(Guid.NewGuid(), UtcNow());
        rev.Approve(UtcNow(60));

        rev.Archive();

        rev.Lifecycle.Should().Be(DocumentLifecycle.Archived);
    }

    [Fact]
    public void Archive_FromDraft_Throws()
    {
        var rev = NewDraftRevision();

        Action act = () => rev.Archive();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Cannot archive revision*");
    }
}
