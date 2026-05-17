using AwesomeAssertions;

using EasySynQ.Domain;
using EasySynQ.Domain.Entities.Documents;
using EasySynQ.Services.Abstractions;
using EasySynQ.Services.Documents;

using Moq;

using Xunit;

namespace EasySynQ.Tests.Unit.Services.Documents;

/// <summary>
/// Unit tests for <see cref="DocumentRevisionLockReasonResolver"/>
/// (ADR 0012 C7a). Exercises every chain template L1, L2, L3, L4,
/// L6 + the Draft "not locked" case. Pure in-memory; mocks the
/// revision repository to fix state and asserts on the resolved
/// chain shape.
/// </summary>
public class DocumentRevisionLockReasonResolverTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly Mock<IDocumentRevisionRepository> _revisions = new(MockBehavior.Strict);

    private DocumentRevisionLockReasonResolver NewResolver()
        => new(_revisions.Object);

    private static DocumentRevision NewDraft(
        Guid revId,
        Guid docId,
        string label = "Rev A")
    {
        return new DocumentRevision(revId, docId, label, Guid.NewGuid());
    }

    private static DocumentRevision NewInReview(
        Guid revId,
        Guid docId,
        string label = "Rev A",
        DateTime? lockedAtUtc = null)
    {
        var rev = NewDraft(revId, docId, label);
        rev.Submit(
            Guid.NewGuid(),
            lockedAtUtc ?? new DateTime(2026, 5, 17, 10, 0, 0, DateTimeKind.Utc));
        return rev;
    }

    private static DocumentRevision NewApproved(
        Guid revId,
        Guid docId,
        string label = "Rev A",
        DateTime? approvedAtUtc = null)
    {
        var rev = NewInReview(revId, docId, label);
        rev.Approve(approvedAtUtc ?? new DateTime(2026, 5, 17, 11, 0, 0, DateTimeKind.Utc));
        return rev;
    }

    private static DocumentRevision NewSuperseded(
        Guid revId,
        Guid docId,
        string label = "Rev A",
        DateTime? approvedAtUtc = null)
    {
        var rev = NewApproved(revId, docId, label, approvedAtUtc);
        rev.Supersede();
        return rev;
    }

    private static DocumentRevision NewArchived(
        Guid revId,
        Guid docId,
        string label = "Rev A")
    {
        var rev = NewApproved(revId, docId, label);
        rev.Archive();
        return rev;
    }

    private static void SetModifiedFields(DocumentRevision rev, DateTime modifiedUtc, string modifiedBy)
    {
        typeof(DocumentRevision).BaseType!.BaseType!
            .GetProperty(nameof(DocumentRevision.ModifiedUtc))!
            .SetValue(rev, modifiedUtc);
        typeof(DocumentRevision).BaseType!.BaseType!
            .GetProperty(nameof(DocumentRevision.ModifiedBy))!
            .SetValue(rev, modifiedBy);
    }

    private static void SetIsDeleted(DocumentRevision rev)
    {
        typeof(DocumentRevision).BaseType!.BaseType!
            .GetProperty(nameof(DocumentRevision.IsDeleted))!
            .SetValue(rev, true);
    }

    [Fact]
    public void LockedEntityType_ReturnsDocumentRevisionConstant()
    {
        var sut = NewResolver();

        sut.LockedEntityType.Should().Be(LockedEntityTypes.DocumentRevision);
    }

    [Fact]
    public async Task UnknownId_ReturnsNullAsync()
    {
        var revId = Guid.NewGuid();
        _revisions
            .Setup(r => r.GetByIdIncludingDeletedAsync(revId, Ct))
            .ReturnsAsync((DocumentRevision?)null);

        var sut = NewResolver();

        var result = await sut.ResolveAsync(revId.ToString("D"), Ct);

        result.Should().BeNull();
    }

    [Fact]
    public async Task NonGuidId_ReturnsNullWithoutRepoCallAsync()
    {
        var sut = NewResolver();

        var result = await sut.ResolveAsync("not-a-guid", Ct);

        result.Should().BeNull();
        _revisions.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DraftRevision_ReturnsNullAsync()
    {
        // L0 — not a lockout; the author may freely edit.
        var revId = Guid.NewGuid();
        var docId = Guid.NewGuid();
        var draft = NewDraft(revId, docId);
        _revisions
            .Setup(r => r.GetByIdIncludingDeletedAsync(revId, Ct))
            .ReturnsAsync(draft);

        var sut = NewResolver();

        var result = await sut.ResolveAsync(revId.ToString("D"), Ct);

        result.Should().BeNull();
    }

    [Fact]
    public async Task InReviewRevision_ReturnsL2TerminalChainAsync()
    {
        var revId = Guid.NewGuid();
        var docId = Guid.NewGuid();
        var lockedAt = new DateTime(2026, 5, 17, 10, 0, 0, DateTimeKind.Utc);
        var rev = NewInReview(revId, docId, label: "Rev A", lockedAtUtc: lockedAt);
        _revisions
            .Setup(r => r.GetByIdIncludingDeletedAsync(revId, Ct))
            .ReturnsAsync(rev);

        var sut = NewResolver();

        var result = await sut.ResolveAsync(revId.ToString("D"), Ct);

        result.Should().NotBeNull();
        result!.LockedEntityType.Should().Be(LockedEntityTypes.DocumentRevision);
        result.LockedEntityId.Should().Be(revId.ToString("D"));
        result.Chain.Should().ContainSingle();
        var link = result.Chain[0];
        link.IsTerminal.Should().BeTrue();
        link.Tag.Should().Be(LockedEntityTypes.DocumentRevision);
        link.Id.Should().Be("Rev A");
        link.Detail.Should().Contain("In Review since 2026-05-17 10:00:00 UTC");
        link.Detail.Should().Contain("Waiting on reviewer signatures");
    }

    [Fact]
    public async Task ApprovedRevision_ReturnsL1TerminalChainAsync()
    {
        var revId = Guid.NewGuid();
        var docId = Guid.NewGuid();
        var approvedAt = new DateTime(2026, 5, 17, 11, 0, 0, DateTimeKind.Utc);
        var rev = NewApproved(revId, docId, label: "Rev B", approvedAtUtc: approvedAt);
        _revisions
            .Setup(r => r.GetByIdIncludingDeletedAsync(revId, Ct))
            .ReturnsAsync(rev);

        var sut = NewResolver();

        var result = await sut.ResolveAsync(revId.ToString("D"), Ct);

        result.Should().NotBeNull();
        result!.Chain.Should().ContainSingle();
        var link = result.Chain[0];
        link.IsTerminal.Should().BeTrue();
        link.Id.Should().Be("Rev B");
        link.Detail.Should().Contain("Approved on 2026-05-17 11:00:00 UTC");
    }

    [Fact]
    public async Task SupersededRevision_WithSuccessor_ReturnsL3TwoLinkChainAsync()
    {
        var revId = Guid.NewGuid();
        var successorId = Guid.NewGuid();
        var docId = Guid.NewGuid();

        var rev = NewSuperseded(revId, docId, label: "Rev A",
            approvedAtUtc: new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc));
        var successor = NewApproved(successorId, docId, label: "Rev B",
            approvedAtUtc: new DateTime(2026, 5, 17, 12, 0, 0, DateTimeKind.Utc));

        _revisions
            .Setup(r => r.GetByIdIncludingDeletedAsync(revId, Ct))
            .ReturnsAsync(rev);
        _revisions
            .Setup(r => r.GetByDocumentIdAsync(docId, Ct))
            .ReturnsAsync(new[] { rev, successor });

        var sut = NewResolver();

        var result = await sut.ResolveAsync(revId.ToString("D"), Ct);

        result.Should().NotBeNull();
        result!.Chain.Should().HaveCount(2);

        var thisLink = result.Chain[0];
        thisLink.IsTerminal.Should().BeFalse();
        thisLink.Tag.Should().Be(LockedEntityTypes.DocumentRevision);
        thisLink.Id.Should().Be("Rev A");
        thisLink.Because.Should().Be("superseded by");
        thisLink.NavigationEntityType.Should().Be(LockedEntityTypes.DocumentRevision);
        thisLink.NavigationEntityId.Should().Be(successorId.ToString("D"));

        var terminal = result.Chain[1];
        terminal.IsTerminal.Should().BeTrue();
        terminal.Id.Should().Be("Rev B");
        terminal.Detail.Should().Contain("Approved on 2026-05-17 12:00:00 UTC");
    }

    [Fact]
    public async Task SupersededRevision_WithoutSuccessor_ReturnsValidTerminalPlaceholderAsync()
    {
        // Defensive — the chain must remain a valid 2-link
        // chain per LockReason's invariants even when the successor
        // can't be located (unexpected but recoverable).
        var revId = Guid.NewGuid();
        var docId = Guid.NewGuid();
        var rev = NewSuperseded(revId, docId);

        _revisions
            .Setup(r => r.GetByIdIncludingDeletedAsync(revId, Ct))
            .ReturnsAsync(rev);
        _revisions
            .Setup(r => r.GetByDocumentIdAsync(docId, Ct))
            .ReturnsAsync(new[] { rev });

        var sut = NewResolver();

        var result = await sut.ResolveAsync(revId.ToString("D"), Ct);

        result.Should().NotBeNull();
        result!.Chain.Should().HaveCount(2);
        result.Chain[0].IsTerminal.Should().BeFalse();
        result.Chain[0].Because.Should().Be("superseded by");
        result.Chain[0].NavigationEntityType.Should().BeNull();
        result.Chain[1].IsTerminal.Should().BeTrue();
        result.Chain[1].Detail.Should().Contain("not be located");
    }

    [Fact]
    public async Task ArchivedRevision_ReturnsL4TwoLinkChainAsync()
    {
        var revId = Guid.NewGuid();
        var docId = Guid.NewGuid();
        var rev = NewArchived(revId, docId, label: "Rev A");

        _revisions
            .Setup(r => r.GetByIdIncludingDeletedAsync(revId, Ct))
            .ReturnsAsync(rev);

        var sut = NewResolver();

        var result = await sut.ResolveAsync(revId.ToString("D"), Ct);

        result.Should().NotBeNull();
        result!.Chain.Should().HaveCount(2);

        result.Chain[0].IsTerminal.Should().BeFalse();
        result.Chain[0].Tag.Should().Be(LockedEntityTypes.DocumentRevision);
        result.Chain[0].Because.Should().Be("parent Document was retired");
        result.Chain[0].NavigationEntityType.Should().Be(LockedEntityTypes.Document);
        result.Chain[0].NavigationEntityId.Should().Be(docId.ToString("D"));

        result.Chain[1].IsTerminal.Should().BeTrue();
        result.Chain[1].Tag.Should().Be(LockedEntityTypes.Document);
        result.Chain[1].Id.Should().Be(docId.ToString("D"));
    }

    [Fact]
    public async Task SoftDeletedRevision_ReturnsL6TerminalChainAsync()
    {
        var revId = Guid.NewGuid();
        var docId = Guid.NewGuid();
        var rev = NewDraft(revId, docId, label: "Rev A");
        SetModifiedFields(rev,
            new DateTime(2026, 5, 17, 9, 30, 0, DateTimeKind.Utc),
            "admin");
        SetIsDeleted(rev);

        _revisions
            .Setup(r => r.GetByIdIncludingDeletedAsync(revId, Ct))
            .ReturnsAsync(rev);

        var sut = NewResolver();

        var result = await sut.ResolveAsync(revId.ToString("D"), Ct);

        result.Should().NotBeNull();
        result!.Chain.Should().ContainSingle();
        result.Chain[0].IsTerminal.Should().BeTrue();
        result.Chain[0].Detail.Should().Contain("Soft-deleted on 2026-05-17 09:30:00 UTC by admin");
    }

    [Fact]
    public async Task SoftDeletedSupersededRevision_L6TakesPrecedenceOverL3Async()
    {
        // Both conditions hold; L6 wins. The resolver should NOT
        // attempt to walk siblings to construct an L3 chain.
        var revId = Guid.NewGuid();
        var docId = Guid.NewGuid();
        var rev = NewSuperseded(revId, docId);
        SetModifiedFields(rev,
            new DateTime(2026, 5, 17, 9, 30, 0, DateTimeKind.Utc),
            "admin");
        SetIsDeleted(rev);

        _revisions
            .Setup(r => r.GetByIdIncludingDeletedAsync(revId, Ct))
            .ReturnsAsync(rev);
        // Deliberately NOT setting up GetByDocumentIdAsync — strict
        // mock fails the test if the resolver tries to call it.

        var sut = NewResolver();

        var result = await sut.ResolveAsync(revId.ToString("D"), Ct);

        result.Should().NotBeNull();
        result!.Chain[0].Detail.Should().Contain("Soft-deleted");
    }

    [Fact]
    public async Task EmptyId_ThrowsAsync()
    {
        var sut = NewResolver();

        var act = async () => await sut.ResolveAsync("", Ct);

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
