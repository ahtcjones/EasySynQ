using AwesomeAssertions;

using EasySynQ.Domain;
using EasySynQ.Domain.Entities.Documents;
using EasySynQ.Services.Abstractions;
using EasySynQ.Services.Documents;

using Moq;

using Xunit;

namespace EasySynQ.Tests.Unit.Services.Documents;

/// <summary>
/// Unit tests for <see cref="DocumentLockReasonResolver"/> (ADR 0012
/// C7a). Pure in-memory; mocks the repository surface to fix entity
/// state and asserts on the resolved chain shape.
/// </summary>
public class DocumentLockReasonResolverTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly Mock<IDocumentRepository> _documents = new(MockBehavior.Strict);

    private DocumentLockReasonResolver NewResolver() => new(_documents.Object);

    private static Document NewDocument(
        Guid id,
        string number = "SOP-001",
        string title = "Test")
    {
        return new Document(id, number, title);
    }

    /// <summary>
    /// Forces standard-fields semantics onto a freshly-constructed
    /// fixture <see cref="Document"/> by writing
    /// <c>ModifiedUtc</c>/<c>ModifiedBy</c> via reflection. In
    /// production these fields are set by the standard-fields
    /// interceptor on save; tests work with detached fixtures so the
    /// interceptor never runs.
    /// </summary>
    private static void SetModifiedFields(Document doc, DateTime modifiedUtc, string modifiedBy)
    {
        typeof(Document).BaseType!
            .GetProperty(nameof(Document.ModifiedUtc))!
            .SetValue(doc, modifiedUtc);
        typeof(Document).BaseType!
            .GetProperty(nameof(Document.ModifiedBy))!
            .SetValue(doc, modifiedBy);
    }

    private static void SetIsDeleted(Document doc)
    {
        typeof(Document).BaseType!
            .GetProperty(nameof(Document.IsDeleted))!
            .SetValue(doc, true);
    }

    [Fact]
    public void LockedEntityType_ReturnsDocumentConstant()
    {
        var sut = NewResolver();

        sut.LockedEntityType.Should().Be(LockedEntityTypes.Document);
    }

    [Fact]
    public async Task UnknownId_ReturnsNullAsync()
    {
        var docId = Guid.NewGuid();
        _documents
            .Setup(r => r.GetByIdIncludingDeletedAsync(docId, Ct))
            .ReturnsAsync((Document?)null);

        var sut = NewResolver();

        var result = await sut.ResolveAsync(docId.ToString("D"), Ct);

        result.Should().BeNull();
    }

    [Fact]
    public async Task NonGuidId_ReturnsNullWithoutRepoCallAsync()
    {
        var sut = NewResolver();

        var result = await sut.ResolveAsync("not-a-guid", Ct);

        result.Should().BeNull();
        _documents.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LiveNonRetiredDocument_ReturnsNullAsync()
    {
        var docId = Guid.NewGuid();
        var doc = NewDocument(docId);
        _documents
            .Setup(r => r.GetByIdIncludingDeletedAsync(docId, Ct))
            .ReturnsAsync(doc);

        var sut = NewResolver();

        var result = await sut.ResolveAsync(docId.ToString("D"), Ct);

        result.Should().BeNull();
    }

    [Fact]
    public async Task RetiredDocument_ReturnsL5TerminalChainAsync()
    {
        var docId = Guid.NewGuid();
        var retiredBy = Guid.NewGuid();
        var retiredAt = new DateTime(2026, 5, 17, 12, 0, 0, DateTimeKind.Utc);
        var doc = NewDocument(docId, number: "SOP-Q-001");
        doc.Retire(retiredAt, retiredBy, Guid.NewGuid());
        _documents
            .Setup(r => r.GetByIdIncludingDeletedAsync(docId, Ct))
            .ReturnsAsync(doc);

        var sut = NewResolver();

        var result = await sut.ResolveAsync(docId.ToString("D"), Ct);

        result.Should().NotBeNull();
        result!.LockedEntityType.Should().Be(LockedEntityTypes.Document);
        result.LockedEntityId.Should().Be(docId.ToString("D"));
        result.Chain.Should().ContainSingle()
            .Which.IsTerminal.Should().BeTrue();
        var link = result.Chain[0];
        link.Tag.Should().Be(LockedEntityTypes.Document);
        link.Id.Should().Be("SOP-Q-001");
        link.Detail.Should().Contain("Retired on 2026-05-17 12:00:00 UTC");
        link.Detail.Should().Contain(retiredBy.ToString("D"));
    }

    [Fact]
    public async Task SoftDeletedDocument_ReturnsL6TerminalChainAsync()
    {
        var docId = Guid.NewGuid();
        var doc = NewDocument(docId, number: "SOP-Q-002");
        SetModifiedFields(doc,
            new DateTime(2026, 5, 17, 9, 30, 0, DateTimeKind.Utc),
            "admin");
        SetIsDeleted(doc);

        _documents
            .Setup(r => r.GetByIdIncludingDeletedAsync(docId, Ct))
            .ReturnsAsync(doc);

        var sut = NewResolver();

        var result = await sut.ResolveAsync(docId.ToString("D"), Ct);

        result.Should().NotBeNull();
        result!.Chain.Should().ContainSingle();
        var link = result.Chain[0];
        link.IsTerminal.Should().BeTrue();
        link.Detail.Should().Contain("Soft-deleted on 2026-05-17 09:30:00 UTC by admin");
    }

    [Fact]
    public async Task SoftDeletedRetiredDocument_L6TakesPrecedenceOverL5Async()
    {
        // Both conditions hold; L6 wins (the soft-delete is the
        // administratively-removed cause, not the retirement).
        var docId = Guid.NewGuid();
        var doc = NewDocument(docId, number: "SOP-Q-003");
        doc.Retire(
            new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc),
            Guid.NewGuid(),
            Guid.NewGuid());
        SetModifiedFields(doc,
            new DateTime(2026, 5, 17, 9, 30, 0, DateTimeKind.Utc),
            "admin");
        SetIsDeleted(doc);

        _documents
            .Setup(r => r.GetByIdIncludingDeletedAsync(docId, Ct))
            .ReturnsAsync(doc);

        var sut = NewResolver();

        var result = await sut.ResolveAsync(docId.ToString("D"), Ct);

        result.Should().NotBeNull();
        // L6 chain mentions soft-delete, not retirement.
        result!.Chain[0].Detail.Should().Contain("Soft-deleted");
        result.Chain[0].Detail.Should().NotContain("Retired on");
    }

    [Fact]
    public async Task EmptyId_ThrowsAsync()
    {
        var sut = NewResolver();

        var act = async () => await sut.ResolveAsync("", Ct);

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
