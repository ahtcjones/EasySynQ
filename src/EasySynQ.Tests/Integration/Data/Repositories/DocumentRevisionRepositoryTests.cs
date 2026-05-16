using AwesomeAssertions;

using EasySynQ.Data.Context;
using EasySynQ.Data.Repositories;
using EasySynQ.Domain.Entities.Documents;
using EasySynQ.Domain.Enums;
using EasySynQ.Tests.Integration.Services;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace EasySynQ.Tests.Integration.Data.Repositories;

/// <summary>
/// Integration tests for
/// <see cref="EasySynQ.Services.Abstractions.IDocumentRevisionRepository.GetActiveRevisionAsync"/>
/// (ADR 0008 C3 plan §C2). Inherits from
/// <see cref="ServiceIntegrationTestBase"/> for the full DI graph.
/// </summary>
/// <remarks>
/// The as-of resolver returns the revision whose <c>Lifecycle ==
/// Approved</c> AND whose effective date has passed (or is null), with
/// the latest <c>ApprovedAtUtc</c> winning ties. Per plan §G Q9,
/// supersede-on-approval semantics mean the resolver returns
/// <see langword="null"/> during the gap between a successor's
/// approval and its effective date.
/// </remarks>
public class DocumentRevisionRepositoryTests : ServiceIntegrationTestBase
{
    private static DocumentRevision NewRevision(
        Guid documentId,
        string label,
        DocumentLifecycle lifecycle,
        DateTime? approvedAtUtc,
        DateTime? effectiveFromUtc)
    {
        var rev = new DocumentRevision(
            id: Guid.NewGuid(),
            documentId: documentId,
            revisionLabel: label,
            authorUserId: Guid.NewGuid());

        // Drive the entity through the appropriate transitions to
        // reach the requested lifecycle. Each transition runs the
        // entity's invariants.
        if (effectiveFromUtc is not null)
        {
            rev.SetEffectiveFromUtc(effectiveFromUtc);
        }

        if (lifecycle == DocumentLifecycle.Draft)
        {
            return rev;
        }

        // Submit → InReview.
        rev.Submit(Guid.NewGuid(), approvedAtUtc ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        if (lifecycle == DocumentLifecycle.InReview)
        {
            return rev;
        }

        rev.Approve(approvedAtUtc!.Value);
        if (lifecycle == DocumentLifecycle.Approved)
        {
            return rev;
        }
        if (lifecycle == DocumentLifecycle.Superseded)
        {
            rev.Supersede();
            return rev;
        }
        if (lifecycle == DocumentLifecycle.Archived)
        {
            rev.Archive();
            return rev;
        }
        throw new InvalidOperationException($"Unhandled target lifecycle: {lifecycle}.");
    }

    private async Task PersistAsync(EasySynQDbContext ctx, params DocumentRevision[] revisions)
    {
        // Set CurrentUser so standard-fields interceptor has a CreatedBy
        // to write. We're in a setup context, identity is arbitrary.
        CurrentUser.UserId = Guid.NewGuid();
        CurrentUser.Username = "tester";
        CurrentUser.Roles = ["TestRole"];

        // DocumentRevision has a hard FK to Document (within-aggregate
        // per ADR 0004). Ensure the parent rows exist before inserting
        // revisions; the test fixture only cares about revision shape.
        var documentIds = revisions.Select(r => r.DocumentId).Distinct().ToList();
        var existingDocs = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .ToListAsync(
                ctx.Documents.Where(d => documentIds.Contains(d.Id)).Select(d => d.Id),
                Ct);
        foreach (var id in documentIds.Except(existingDocs))
        {
            ctx.Documents.Add(new Document(id, $"DOC-{id:N}".Substring(0, 16), "Fixture Document"));
        }
        foreach (var r in revisions)
        {
            ctx.DocumentRevisions.Add(r);
        }
        await ctx.SaveChangesAsync(Ct);
    }

    private static async Task<DocumentRevision?> ResolveAsync(
        AsyncServiceScope scope,
        Guid documentId,
        DateTime asOfUtc,
        CancellationToken ct)
    {
        var repo = scope.ServiceProvider
            .GetRequiredService<EasySynQ.Services.Abstractions.IDocumentRevisionRepository>();
        return await repo.GetActiveRevisionAsync(documentId, asOfUtc, ct);
    }

    [Fact]
    public async Task GetActiveRevision_NoRevisions_ReturnsNullAsync()
    {
        var documentId = Guid.NewGuid();
        await using var scope = NewScope();
        var result = await ResolveAsync(scope, documentId, Clock.UtcNow, Ct);
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetActiveRevision_OnlyDraftRevisions_ReturnsNullAsync()
    {
        var documentId = Guid.NewGuid();
        await using (var ctx = NewContext())
        {
            await PersistAsync(ctx,
                NewRevision(documentId, "Rev A", DocumentLifecycle.Draft, null, null));
        }

        await using var scope = NewScope();
        var result = await ResolveAsync(scope, documentId, Clock.UtcNow, Ct);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetActiveRevision_ApprovedNoEffectiveDate_ReturnsThatRevisionAsync()
    {
        var documentId = Guid.NewGuid();
        var approvedAt = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        var rev = NewRevision(documentId, "Rev A", DocumentLifecycle.Approved, approvedAt, effectiveFromUtc: null);
        await using (var ctx = NewContext())
        {
            await PersistAsync(ctx, rev);
        }

        await using var scope = NewScope();
        var result = await ResolveAsync(scope, documentId, asOfUtc: approvedAt.AddDays(7), Ct);

        result.Should().NotBeNull();
        result!.Id.Should().Be(rev.Id);
    }

    [Fact]
    public async Task GetActiveRevision_ApprovedEffectiveInPast_ReturnsThatRevisionAsync()
    {
        var documentId = Guid.NewGuid();
        var approvedAt = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        var effective = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var rev = NewRevision(documentId, "Rev A", DocumentLifecycle.Approved, approvedAt, effective);
        await using (var ctx = NewContext())
        {
            await PersistAsync(ctx, rev);
        }

        await using var scope = NewScope();
        var result = await ResolveAsync(scope, documentId, asOfUtc: effective.AddDays(7), Ct);

        result.Should().NotBeNull();
        result!.Id.Should().Be(rev.Id);
    }

    [Fact]
    public async Task GetActiveRevision_ApprovedEffectiveInFuture_ReturnsNullAtAsOfBeforeEffectiveAsync()
    {
        var documentId = Guid.NewGuid();
        var approvedAt = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        var effective = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var rev = NewRevision(documentId, "Rev A", DocumentLifecycle.Approved, approvedAt, effective);
        await using (var ctx = NewContext())
        {
            await PersistAsync(ctx, rev);
        }

        await using var scope = NewScope();

        // As-of before EffectiveFromUtc → null.
        var beforeEffective = await ResolveAsync(scope, documentId, asOfUtc: effective.AddDays(-1), Ct);
        beforeEffective.Should().BeNull();

        // As-of after EffectiveFromUtc → returns the revision.
        var afterEffective = await ResolveAsync(scope, documentId, asOfUtc: effective.AddDays(1), Ct);
        afterEffective.Should().NotBeNull();
        afterEffective!.Id.Should().Be(rev.Id);
    }

    [Fact]
    public async Task GetActiveRevision_TwoApprovedRevisions_ReturnsLatestApprovedAsync()
    {
        var documentId = Guid.NewGuid();
        var approvedA = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        var approvedB = new DateTime(2026, 3, 20, 10, 0, 0, DateTimeKind.Utc);
        var revA = NewRevision(documentId, "Rev A", DocumentLifecycle.Approved, approvedA, null);
        var revB = NewRevision(documentId, "Rev B", DocumentLifecycle.Approved, approvedB, null);
        await using (var ctx = NewContext())
        {
            await PersistAsync(ctx, revA, revB);
        }

        await using var scope = NewScope();
        var result = await ResolveAsync(scope, documentId, asOfUtc: approvedB.AddDays(7), Ct);

        result.Should().NotBeNull();
        result!.Id.Should().Be(revB.Id);
    }

    [Fact]
    public async Task GetActiveRevision_OneApprovedOneSuperseded_ReturnsApprovedAsync()
    {
        var documentId = Guid.NewGuid();
        var approvedA = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        var approvedB = new DateTime(2026, 3, 20, 10, 0, 0, DateTimeKind.Utc);
        var revA = NewRevision(documentId, "Rev A", DocumentLifecycle.Superseded, approvedA, null);
        var revB = NewRevision(documentId, "Rev B", DocumentLifecycle.Approved, approvedB, null);
        await using (var ctx = NewContext())
        {
            await PersistAsync(ctx, revA, revB);
        }

        await using var scope = NewScope();
        var result = await ResolveAsync(scope, documentId, asOfUtc: approvedB.AddDays(7), Ct);

        result.Should().NotBeNull();
        result!.Id.Should().Be(revB.Id);
    }

    [Fact]
    public async Task GetActiveRevision_ArchivedRevision_ReturnsNullAsync()
    {
        var documentId = Guid.NewGuid();
        var approvedAt = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        var rev = NewRevision(documentId, "Rev A", DocumentLifecycle.Archived, approvedAt, null);
        await using (var ctx = NewContext())
        {
            await PersistAsync(ctx, rev);
        }

        await using var scope = NewScope();
        var result = await ResolveAsync(scope, documentId, asOfUtc: approvedAt.AddDays(7), Ct);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetActiveRevision_GapBetweenSupersedeAndEffective_ReturnsNullAsync()
    {
        // Plan §G Q9 documented behavior: a successor that is Approved
        // but not yet Effective leaves the document with no Active
        // revision (prior is Superseded, successor isn't Active yet).
        var documentId = Guid.NewGuid();
        var approvedA = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        var approvedB = new DateTime(2026, 3, 20, 10, 0, 0, DateTimeKind.Utc);
        var effectiveB = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var revA = NewRevision(documentId, "Rev A", DocumentLifecycle.Superseded, approvedA, null);
        var revB = NewRevision(documentId, "Rev B", DocumentLifecycle.Approved, approvedB, effectiveB);
        await using (var ctx = NewContext())
        {
            await PersistAsync(ctx, revA, revB);
        }

        await using var scope = NewScope();

        // During the gap: Rev A is Superseded (excluded), Rev B is
        // Approved-but-not-yet-effective (excluded). Resolver returns
        // null — exactly as documented.
        var inGap = await ResolveAsync(scope, documentId, asOfUtc: approvedB.AddDays(7), Ct);
        inGap.Should().BeNull();

        // After Rev B's effective date: Rev B becomes the resolver's
        // answer.
        var afterEffective = await ResolveAsync(scope, documentId, asOfUtc: effectiveB.AddDays(1), Ct);
        afterEffective.Should().NotBeNull();
        afterEffective!.Id.Should().Be(revB.Id);
    }

    // ─── GetLatestRevisionAsync (C6a) ───────────────────────────────

    private static async Task<DocumentRevision?> LatestAsync(
        AsyncServiceScope scope,
        Guid documentId,
        CancellationToken ct)
    {
        var repo = scope.ServiceProvider
            .GetRequiredService<EasySynQ.Services.Abstractions.IDocumentRevisionRepository>();
        return await repo.GetLatestRevisionAsync(documentId, ct);
    }

    [Fact]
    public async Task GetLatestRevision_NoRevisions_ReturnsNullAsync()
    {
        var documentId = Guid.NewGuid();
        await using (var ctx = NewContext())
        {
            // Insert the parent document so the resolver runs against a
            // real row; the answer should still be null with no
            // revisions present.
            CurrentUser.UserId = Guid.NewGuid();
            CurrentUser.Username = "tester";
            CurrentUser.Roles = ["TestRole"];
            ctx.Documents.Add(new Document(documentId, "DOC-EMPTY-A", "Empty"));
            await ctx.SaveChangesAsync(Ct);
        }

        await using var scope = NewScope();
        var result = await LatestAsync(scope, documentId, Ct);
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetLatestRevision_SingleDraft_ReturnsItAsync()
    {
        var documentId = Guid.NewGuid();
        var draft = NewRevision(documentId, "Rev A", DocumentLifecycle.Draft, null, null);
        await using (var ctx = NewContext())
        {
            await PersistAsync(ctx, draft);
        }

        await using var scope = NewScope();
        var result = await LatestAsync(scope, documentId, Ct);

        result.Should().NotBeNull();
        result!.Id.Should().Be(draft.Id);
        result.Lifecycle.Should().Be(DocumentLifecycle.Draft);
    }

    [Fact]
    public async Task GetLatestRevision_ReturnsMostRecentRegardlessOfLifecycleAsync()
    {
        // Plant Rev A (Superseded, earlier CreatedUtc) and Rev B
        // (Approved, later CreatedUtc). GetLatestRevisionAsync should
        // return Rev B because it was created later, even though both
        // are non-Draft states.
        var documentId = Guid.NewGuid();
        var approvedA = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        var approvedB = new DateTime(2026, 3, 20, 10, 0, 0, DateTimeKind.Utc);
        var revA = NewRevision(documentId, "Rev A", DocumentLifecycle.Superseded, approvedA, null);
        var revB = NewRevision(documentId, "Rev B", DocumentLifecycle.Approved, approvedB, null);

        // Persist Rev A first (earlier CreatedUtc), then advance the
        // clock and persist Rev B (later CreatedUtc).
        Clock.UtcNow = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await using (var ctx = NewContext())
        {
            await PersistAsync(ctx, revA);
        }
        Clock.UtcNow = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        await using (var ctx = NewContext())
        {
            await PersistAsync(ctx, revB);
        }

        await using var scope = NewScope();
        var result = await LatestAsync(scope, documentId, Ct);

        result.Should().NotBeNull();
        result!.Id.Should().Be(revB.Id);
    }

    [Fact]
    public async Task GetLatestRevision_GuidEmpty_ThrowsArgumentExceptionAsync()
    {
        await using var scope = NewScope();
        var repo = scope.ServiceProvider
            .GetRequiredService<EasySynQ.Services.Abstractions.IDocumentRevisionRepository>();

        Func<Task> act = async () => await repo.GetLatestRevisionAsync(Guid.Empty, Ct);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ─── GetByDocumentIdAsync (C6a) ─────────────────────────────────

    [Fact]
    public async Task GetByDocumentId_NoRevisions_ReturnsEmptyAsync()
    {
        var documentId = Guid.NewGuid();
        await using var scope = NewScope();
        var repo = scope.ServiceProvider
            .GetRequiredService<EasySynQ.Services.Abstractions.IDocumentRevisionRepository>();
        var result = await repo.GetByDocumentIdAsync(documentId, Ct);
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByDocumentId_TwoRevisions_ReturnsBothOrderedByCreatedAsync()
    {
        var documentId = Guid.NewGuid();
        var revA = NewRevision(documentId, "Rev A", DocumentLifecycle.Draft, null, null);
        var revB = NewRevision(documentId, "Rev B", DocumentLifecycle.Draft, null, null);

        Clock.UtcNow = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await using (var ctx = NewContext())
        {
            await PersistAsync(ctx, revA);
        }
        Clock.UtcNow = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        await using (var ctx = NewContext())
        {
            await PersistAsync(ctx, revB);
        }

        await using var scope = NewScope();
        var repo = scope.ServiceProvider
            .GetRequiredService<EasySynQ.Services.Abstractions.IDocumentRevisionRepository>();
        var result = await repo.GetByDocumentIdAsync(documentId, Ct);

        result.Should().HaveCount(2);
        result[0].Id.Should().Be(revA.Id);
        result[1].Id.Should().Be(revB.Id);
    }

    [Fact]
    public async Task GetByDocumentId_DoesNotReturnOtherDocumentsRevisionsAsync()
    {
        var docA = Guid.NewGuid();
        var docB = Guid.NewGuid();
        var revInA = NewRevision(docA, "Rev A", DocumentLifecycle.Draft, null, null);
        var revInB = NewRevision(docB, "Rev A", DocumentLifecycle.Draft, null, null);

        await using (var ctx = NewContext())
        {
            await PersistAsync(ctx, revInA, revInB);
        }

        await using var scope = NewScope();
        var repo = scope.ServiceProvider
            .GetRequiredService<EasySynQ.Services.Abstractions.IDocumentRevisionRepository>();
        var result = await repo.GetByDocumentIdAsync(docA, Ct);

        result.Should().ContainSingle().Which.Id.Should().Be(revInA.Id);
    }

    [Fact]
    public async Task GetByDocumentId_GuidEmpty_ThrowsArgumentExceptionAsync()
    {
        await using var scope = NewScope();
        var repo = scope.ServiceProvider
            .GetRequiredService<EasySynQ.Services.Abstractions.IDocumentRevisionRepository>();

        Func<Task> act = async () => await repo.GetByDocumentIdAsync(Guid.Empty, Ct);
        await act.Should().ThrowAsync<ArgumentException>();
    }
}
