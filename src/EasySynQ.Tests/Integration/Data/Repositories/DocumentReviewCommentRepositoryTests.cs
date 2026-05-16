using AwesomeAssertions;

using EasySynQ.Data.Repositories;
using EasySynQ.Domain.Entities.Documents;
using EasySynQ.Tests.Integration.Data.Interceptors;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace EasySynQ.Tests.Integration.Data.Repositories;

/// <summary>
/// Integration tests for <see cref="DocumentReviewCommentRepository"/>
/// (ADR 0008 C6b). Covers the per-revision fetch shape — empty
/// result, multiple comments, soft-delete filter, foreign-revision
/// isolation, and Guid.Empty validation.
/// </summary>
public class DocumentReviewCommentRepositoryTests : InterceptorIntegrationTestBase
{
    private static DateTime Utc(int hour) =>
        new(2026, 5, 16, hour, 0, 0, DateTimeKind.Utc);

    /// <summary>Seeds a Document + Draft DocumentRevision in this test's
    /// database; returns the revision id. Comments hard-FK the
    /// revision row per the entity configuration, so callers can't
    /// fabricate random revision ids. Document.Number has a unique
    /// index per ADR 0008 — each seeded Document gets a guid-suffix
    /// number so repeated calls within a test don't collide.</summary>
    private async Task<Guid> SeedRevisionAsync()
    {
        var docId = Guid.NewGuid();
        var doc = new Document(docId, $"SOP-{docId:N}".Substring(0, 12), "Test");
        var rev = new DocumentRevision(
            Guid.NewGuid(), doc.Id, "Rev A", Guid.NewGuid());
        await using var ctx = NewContext();
        ctx.Documents.Add(doc);
        ctx.DocumentRevisions.Add(rev);
        await ctx.SaveChangesAsync(Ct);
        return rev.Id;
    }

    [Fact]
    public async Task GetByRevisionIdAsync_NoComments_ReturnsEmptyAsync()
    {
        var revisionId = await SeedRevisionAsync();

        await using var ctx = NewContext();
        var repo = new DocumentReviewCommentRepository(ctx);

        var result = await repo.GetByRevisionIdAsync(revisionId, Ct);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByRevisionIdAsync_ReturnsAllCommentsForRevisionAsync()
    {
        var revisionId = await SeedRevisionAsync();
        var authorId = Guid.NewGuid();
        var c1 = new DocumentReviewComment(
            Guid.NewGuid(), revisionId, authorId, "first", Utc(10));
        var c2 = new DocumentReviewComment(
            Guid.NewGuid(), revisionId, authorId, "second", Utc(11));
        var c3 = new DocumentReviewComment(
            Guid.NewGuid(), revisionId, authorId, "third", Utc(12));

        await using (var ctx = NewContext())
        {
            ctx.DocumentReviewComments.AddRange(c1, c2, c3);
            await ctx.SaveChangesAsync(Ct);
        }

        await using var ctx2 = NewContext();
        var repo = new DocumentReviewCommentRepository(ctx2);
        var result = await repo.GetByRevisionIdAsync(revisionId, Ct);

        result.Should().HaveCount(3);
        result.Select(c => c.Id).Should().BeEquivalentTo([c1.Id, c2.Id, c3.Id]);
    }

    [Fact]
    public async Task GetByRevisionIdAsync_IsolatesByRevisionAsync()
    {
        var revisionA = await SeedRevisionAsync();
        var revisionB = await SeedRevisionAsync();
        var authorId = Guid.NewGuid();
        var onA = new DocumentReviewComment(
            Guid.NewGuid(), revisionA, authorId, "on A", Utc(10));
        var onB = new DocumentReviewComment(
            Guid.NewGuid(), revisionB, authorId, "on B", Utc(10));

        await using (var ctx = NewContext())
        {
            ctx.DocumentReviewComments.AddRange(onA, onB);
            await ctx.SaveChangesAsync(Ct);
        }

        await using var ctx2 = NewContext();
        var repo = new DocumentReviewCommentRepository(ctx2);
        var result = await repo.GetByRevisionIdAsync(revisionA, Ct);

        result.Should().ContainSingle().Which.Id.Should().Be(onA.Id);
    }

    [Fact]
    public async Task GetByRevisionIdAsync_ExcludesSoftDeletedAsync()
    {
        var revisionId = await SeedRevisionAsync();
        var authorId = Guid.NewGuid();
        var alive = new DocumentReviewComment(
            Guid.NewGuid(), revisionId, authorId, "alive", Utc(10));
        var deleted = new DocumentReviewComment(
            Guid.NewGuid(), revisionId, authorId, "deleted", Utc(11));

        await using (var ctx = NewContext())
        {
            ctx.DocumentReviewComments.AddRange(alive, deleted);
            await ctx.SaveChangesAsync(Ct);

            ctx.Entry(deleted)
               .Property(nameof(DocumentReviewComment.IsDeleted))
               .CurrentValue = true;
            await ctx.SaveChangesAsync(Ct);
        }

        await using var ctx2 = NewContext();
        var repo = new DocumentReviewCommentRepository(ctx2);
        var result = await repo.GetByRevisionIdAsync(revisionId, Ct);

        result.Should().ContainSingle().Which.Id.Should().Be(alive.Id);
    }

    [Fact]
    public async Task GetByRevisionIdAsync_EmptyGuid_ThrowsAsync()
    {
        await using var ctx = NewContext();
        var repo = new DocumentReviewCommentRepository(ctx);

        Func<Task> act = async () =>
            await repo.GetByRevisionIdAsync(Guid.Empty, Ct);

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
