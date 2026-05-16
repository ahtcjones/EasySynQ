using System.IO;
using System.Text;

using AwesomeAssertions;

using EasySynQ.Domain;
using EasySynQ.Domain.Entities.Documents;
using EasySynQ.Domain.Enums;
using EasySynQ.Services.Authorization;
using EasySynQ.Services.Documents;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace EasySynQ.Tests.Integration.Services.Documents;

/// <summary>
/// Integration tests for the four C6a additions to
/// <see cref="DocumentLifecycleService"/>: <c>CreateDocumentAsync</c>,
/// <c>AttachPdfToDraftAsync</c>, <c>EditDraftMetadataAsync</c>, and
/// <c>HardDeleteDraftAsync</c> (ADR 0008 C6a). Pinned alongside the
/// existing C3 transition tests rather than merged into them so the
/// file scopes cleanly to the C6a author-working-alone surface.
/// </summary>
/// <remarks>
/// <para>
/// <b>Audit-row counts.</b> Per-operation counts are pinned as
/// composition expressions (e.g., "1 Document Insert + 1 Revision
/// Insert = 2"), following the C3 historical-vs-current lesson — a
/// future schema change that shifts the count surfaces here loudly
/// rather than silently miscounting compliance evidence.
/// </para>
/// </remarks>
public class DocumentLifecycleServiceC6aTests : ServiceIntegrationTestBase
{
    private static readonly IReadOnlyList<string> AuthorPermissions =
    [
        PermissionNames.DocumentCreate,
        PermissionNames.DocumentEditDraft,
        PermissionNames.DocumentHardDelete,
    ];

    private const string AuthorRole = "TestAuthor";

    /// <summary>Configures CurrentUser as the supplied author identity
    /// with the C6a author permission set.</summary>
    private void BecomeAuthor(Guid authorId)
    {
        CurrentUser.UserId = authorId;
        CurrentUser.Username = $"author-{authorId:N}".Substring(0, 16);
        CurrentUser.Roles = [AuthorRole];
        CurrentUser.Permissions = AuthorPermissions.ToList();
        CurrentUser.RolePermissions = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.Ordinal)
        {
            [AuthorRole] = AuthorPermissions.ToList(),
        };
    }

    private async Task<List<EasySynQ.Domain.Entities.Audit.AuditLogEntry>> AuditRowsByCorrelationAsync(
        Guid correlationId)
    {
        await using var ctx = NewContext();
        return await ctx.AuditLogEntries
            .Where(a => a.CorrelationId == correlationId)
            .ToListAsync(Ct);
    }

    private static IDocumentLifecycleService Lifecycle(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IDocumentLifecycleService>();

    private static MemoryStream MakePdfStream(string content)
        => new(Encoding.UTF8.GetBytes(content));

    // ─── CreateDocumentAsync ────────────────────────────────────────

    [Fact]
    public async Task Create_HappyPath_PersistsDocumentAndDraftRevisionAsync()
    {
        var authorId = Guid.NewGuid();
        BecomeAuthor(authorId);
        var corr = Guid.NewGuid();
        Correlation.CurrentCorrelationId = corr;

        Document created;
        await using (var scope = NewScope())
        {
            created = await Lifecycle(scope).CreateDocumentAsync(
                number: "SOP-Q-001",
                title: "Quality Plan",
                cancellationToken: Ct);
        }

        created.Should().NotBeNull();
        created.Number.Should().Be("SOP-Q-001");
        created.Title.Should().Be("Quality Plan");
        created.RetiredAtUtc.Should().BeNull();

        await using (var ctx = NewContext())
        {
            var doc = await ctx.Documents.SingleAsync(d => d.Id == created.Id, Ct);
            doc.Number.Should().Be("SOP-Q-001");
            doc.Title.Should().Be("Quality Plan");
            doc.CreatedBy.Should().Be(authorId.ToString());

            var rev = await ctx.DocumentRevisions
                .SingleAsync(r => r.DocumentId == created.Id, Ct);
            rev.Lifecycle.Should().Be(DocumentLifecycle.Draft);
            rev.RevisionLabel.Should().Be("Rev A");
            rev.AuthorUserId.Should().Be(authorId);
            rev.VaultBlobId.Should().BeNull();
            rev.AuthorSignatureId.Should().BeNull();
            rev.ApprovedAtUtc.Should().BeNull();
            rev.LockedAtUtc.Should().BeNull();
        }

        // Audit row count = 2 (Document Insert + DocumentRevision
        // Insert), both sharing the test-set CorrelationId.
        var rows = await AuditRowsByCorrelationAsync(corr);
        rows.Count.Should().Be(2);
        rows.Should().OnlyContain(r => r.CorrelationId == corr);
        rows.Should().OnlyContain(r => r.Action == AuditAction.Insert);
    }

    [Fact]
    public async Task Create_MissingPermission_ThrowsAsync()
    {
        var authorId = Guid.NewGuid();
        BecomeAuthor(authorId);
        CurrentUser.Permissions = AuthorPermissions
            .Where(p => p != PermissionNames.DocumentCreate)
            .ToList();

        await using var scope = NewScope();
        Func<Task> act = async () => await Lifecycle(scope).CreateDocumentAsync(
            "SOP-X", "Test", Ct);

        var ex = (await act.Should().ThrowAsync<UnauthorizedOperationException>()).Subject.Single();
        ex.PermissionName.Should().Be(PermissionNames.DocumentCreate);
    }

    [Theory]
    [InlineData("", "Title")]
    [InlineData("   ", "Title")]
    [InlineData("SOP-1", "")]
    [InlineData("SOP-1", "   ")]
    public async Task Create_EmptyOrWhitespaceArgs_ThrowsArgumentExceptionAsync(string number, string title)
    {
        BecomeAuthor(Guid.NewGuid());

        await using var scope = NewScope();
        Func<Task> act = async () => await Lifecycle(scope).CreateDocumentAsync(number, title, Ct);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Create_UnauthenticatedCaller_ThrowsInvalidOperationAsync()
    {
        CurrentUser.UserId = null;
        CurrentUser.Username = string.Empty;
        CurrentUser.Roles = [];
        CurrentUser.Permissions = [];

        await using var scope = NewScope();
        Func<Task> act = async () => await Lifecycle(scope).CreateDocumentAsync(
            "SOP-X", "Test", Ct);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ICurrentUserAccessor.UserId is null*");
    }

    // ─── AttachPdfToDraftAsync ──────────────────────────────────────

    [Fact]
    public async Task AttachPdf_FreshContent_StoresAndUpdatesRevisionAsync()
    {
        var authorId = Guid.NewGuid();
        BecomeAuthor(authorId);

        Document doc;
        await using (var scope = NewScope())
        {
            doc = await Lifecycle(scope).CreateDocumentAsync("SOP-Q-002", "Procedure", Ct);
        }

        Guid revisionId;
        await using (var ctx = NewContext())
        {
            revisionId = await ctx.DocumentRevisions
                .Where(r => r.DocumentId == doc.Id)
                .Select(r => r.Id)
                .SingleAsync(Ct);
        }

        var corr = Guid.NewGuid();
        Correlation.CurrentCorrelationId = corr;

        DocumentRevision updated;
        await using (var scope = NewScope())
        {
            using var content = MakePdfStream("hello pdf content");
            updated = await Lifecycle(scope).AttachPdfToDraftAsync(
                revisionId, content, "procedure.pdf", Ct);
        }

        updated.VaultBlobId.Should().NotBeNull();

        await using (var ctx = NewContext())
        {
            var rev = await ctx.DocumentRevisions.SingleAsync(r => r.Id == revisionId, Ct);
            rev.VaultBlobId.Should().Be(updated.VaultBlobId);
            rev.Lifecycle.Should().Be(DocumentLifecycle.Draft);

            var blob = await ctx.VaultBlobs.SingleAsync(b => b.Id == rev.VaultBlobId!.Value, Ct);
            blob.MimeType.Should().Be("application/pdf");
            blob.OriginalFileName.Should().Be("procedure.pdf");
        }

        // Audit composition for fresh content = 2:
        // - VaultBlob Insert (under VaultService.StoreAsync's SaveChanges)
        // - DocumentRevision Update (under Lifecycle.AttachPdfToDraftAsync's
        //   SaveChanges)
        // Both share the test-set CorrelationId because the test
        // correlation provider is mutable-singleton and remains set
        // across both SaveChanges in this DI scope. In production
        // (per-save fallback), the two transactions would get distinct
        // correlation ids — but in the test scenario where corr is
        // pinned externally, both saves observe it.
        var rows = await AuditRowsByCorrelationAsync(corr);
        rows.Count.Should().Be(2);
        rows.Select(r => r.Action).Should().BeEquivalentTo(
            [AuditAction.Insert, AuditAction.Update]);
        rows.Select(r => r.EntityTypeName).Should().BeEquivalentTo(
            [nameof(VaultBlob), nameof(DocumentRevision)]);
    }

    [Fact]
    public async Task AttachPdf_DedupHit_ReusesExistingBlobAsync()
    {
        var authorId = Guid.NewGuid();
        BecomeAuthor(authorId);

        // Two documents, same PDF content. The second attach must
        // observe a dedup hit: same blob row reused, no second on-disk
        // file created.
        Document docA, docB;
        await using (var scope = NewScope())
        {
            docA = await Lifecycle(scope).CreateDocumentAsync("SOP-D-001", "A", Ct);
            docB = await Lifecycle(scope).CreateDocumentAsync("SOP-D-002", "B", Ct);
        }

        Guid revA, revB;
        await using (var ctx = NewContext())
        {
            revA = await ctx.DocumentRevisions
                .Where(r => r.DocumentId == docA.Id).Select(r => r.Id).SingleAsync(Ct);
            revB = await ctx.DocumentRevisions
                .Where(r => r.DocumentId == docB.Id).Select(r => r.Id).SingleAsync(Ct);
        }

        const string content = "identical content for dedup test";

        Guid blobIdA, blobIdB;
        await using (var scope = NewScope())
        {
            using var s = MakePdfStream(content);
            var r = await Lifecycle(scope).AttachPdfToDraftAsync(revA, s, "a.pdf", Ct);
            blobIdA = r.VaultBlobId!.Value;
        }
        await using (var scope = NewScope())
        {
            using var s = MakePdfStream(content);
            var r = await Lifecycle(scope).AttachPdfToDraftAsync(revB, s, "b.pdf", Ct);
            blobIdB = r.VaultBlobId!.Value;
        }

        blobIdB.Should().Be(blobIdA, "identical content must dedup to the same VaultBlob row");

        await using (var ctx = NewContext())
        {
            // Exactly one VaultBlob row exists for the shared content.
            var blobs = await ctx.VaultBlobs.ToListAsync(Ct);
            blobs.Where(b => b.Id == blobIdA).Should().HaveCount(1);
        }
    }

    [Fact]
    public async Task AttachPdf_ReplacePdf_OverwritesVaultBlobIdAsync()
    {
        var authorId = Guid.NewGuid();
        BecomeAuthor(authorId);

        Document doc;
        await using (var scope = NewScope())
        {
            doc = await Lifecycle(scope).CreateDocumentAsync("SOP-R-001", "Replace test", Ct);
        }

        Guid revisionId;
        await using (var ctx = NewContext())
        {
            revisionId = await ctx.DocumentRevisions
                .Where(r => r.DocumentId == doc.Id).Select(r => r.Id).SingleAsync(Ct);
        }

        Guid firstBlobId;
        await using (var scope = NewScope())
        {
            using var s = MakePdfStream("first version");
            var r = await Lifecycle(scope).AttachPdfToDraftAsync(revisionId, s, "v1.pdf", Ct);
            firstBlobId = r.VaultBlobId!.Value;
        }

        Guid secondBlobId;
        await using (var scope = NewScope())
        {
            using var s = MakePdfStream("second version with different content");
            var r = await Lifecycle(scope).AttachPdfToDraftAsync(revisionId, s, "v2.pdf", Ct);
            secondBlobId = r.VaultBlobId!.Value;
        }

        secondBlobId.Should().NotBe(firstBlobId, "different content produces a different blob row");

        await using (var ctx = NewContext())
        {
            var rev = await ctx.DocumentRevisions.SingleAsync(r => r.Id == revisionId, Ct);
            rev.VaultBlobId.Should().Be(secondBlobId);

            // The first blob row remains in the database — orphan
            // cleanup is deferred per the C6a "deferred indefinitely"
            // decision. Asserting its persistence pins the documented
            // behavior so a future cleanup-on-replace change surfaces
            // here loudly.
            var firstBlobStillPresent = await ctx.VaultBlobs
                .AnyAsync(b => b.Id == firstBlobId, Ct);
            firstBlobStillPresent.Should().BeTrue(
                "the prior blob is intentionally left in the vault per the C6a cleanup decision");
        }
    }

    [Fact]
    public async Task AttachPdf_MissingPermission_ThrowsAsync()
    {
        var authorId = Guid.NewGuid();
        BecomeAuthor(authorId);

        Document doc;
        await using (var scope = NewScope())
        {
            doc = await Lifecycle(scope).CreateDocumentAsync("SOP-P-001", "Perm test", Ct);
        }

        Guid revisionId;
        await using (var ctx = NewContext())
        {
            revisionId = await ctx.DocumentRevisions
                .Where(r => r.DocumentId == doc.Id).Select(r => r.Id).SingleAsync(Ct);
        }

        CurrentUser.Permissions = AuthorPermissions
            .Where(p => p != PermissionNames.DocumentEditDraft)
            .ToList();

        await using var scope2 = NewScope();
        using var content = MakePdfStream("data");
        Func<Task> act = async () => await Lifecycle(scope2).AttachPdfToDraftAsync(
            revisionId, content, "f.pdf", Ct);

        var ex = (await act.Should().ThrowAsync<UnauthorizedOperationException>()).Subject.Single();
        ex.PermissionName.Should().Be(PermissionNames.DocumentEditDraft);
    }

    [Fact]
    public async Task AttachPdf_RevisionNotFound_ThrowsKeyNotFoundAsync()
    {
        BecomeAuthor(Guid.NewGuid());

        await using var scope = NewScope();
        using var content = MakePdfStream("data");
        Func<Task> act = async () => await Lifecycle(scope).AttachPdfToDraftAsync(
            Guid.NewGuid(), content, "f.pdf", Ct);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task AttachPdf_RevisionNotInDraft_ThrowsInvalidOperationAsync()
    {
        // Seed a Document + a non-Draft revision by hand. Using the
        // standard service won't get us out of Draft because C6a only
        // ships the Draft side; we drop down to a direct DbContext
        // write to plant an InReview row for this guard test.
        var authorId = Guid.NewGuid();
        BecomeAuthor(authorId);

        var docId = Guid.NewGuid();
        var revisionId = Guid.NewGuid();
        var sigId = Guid.NewGuid();

        await using (var ctx = NewContext())
        {
            ctx.Documents.Add(new Document(docId, "SOP-NS-001", "Not Draft"));
            var rev = new DocumentRevision(revisionId, docId, "Rev A", authorId);
            // Use Submit to drive the revision past Draft — needs a
            // signature row to reference.
            ctx.Signatures.Add(new EasySynQ.Domain.Entities.Audit.Signature(
                id: sigId,
                utcTimestamp: Clock.UtcNow,
                roleAtTimeOfSign: AuthorRole,
                signedEntityType: nameof(DocumentRevision),
                signedEntityId: revisionId.ToString(),
                payloadHash: new string('a', 64)));
            rev.Submit(sigId, Clock.UtcNow);
            ctx.DocumentRevisions.Add(rev);
            await ctx.SaveChangesAsync(Ct);
        }

        await using var scope = NewScope();
        using var content = MakePdfStream("data");
        Func<Task> act = async () => await Lifecycle(scope).AttachPdfToDraftAsync(
            revisionId, content, "f.pdf", Ct);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*expected 'Draft'*");
    }

    // ─── EditDraftMetadataAsync ─────────────────────────────────────

    [Fact]
    public async Task EditMetadata_HappyPath_UpdatesNumberAndTitleAsync()
    {
        var authorId = Guid.NewGuid();
        BecomeAuthor(authorId);

        Document doc;
        await using (var scope = NewScope())
        {
            doc = await Lifecycle(scope).CreateDocumentAsync("SOP-OLD", "Old Title", Ct);
        }

        var corr = Guid.NewGuid();
        Correlation.CurrentCorrelationId = corr;

        await using (var scope = NewScope())
        {
            await Lifecycle(scope).EditDraftMetadataAsync(
                doc.Id, "SOP-NEW", "New Title", Ct);
        }

        await using (var ctx = NewContext())
        {
            var updated = await ctx.Documents.SingleAsync(d => d.Id == doc.Id, Ct);
            updated.Number.Should().Be("SOP-NEW");
            updated.Title.Should().Be("New Title");
        }

        // Audit row count = 1 (Document Update). The latest-revision
        // read is a no-write lookup; no audit row.
        var rows = await AuditRowsByCorrelationAsync(corr);
        rows.Count.Should().Be(1);
        rows.Single().Action.Should().Be(AuditAction.Update);
        rows.Single().EntityTypeName.Should().Be(nameof(Document));
    }

    [Fact]
    public async Task EditMetadata_MissingPermission_ThrowsAsync()
    {
        var authorId = Guid.NewGuid();
        BecomeAuthor(authorId);

        Document doc;
        await using (var scope = NewScope())
        {
            doc = await Lifecycle(scope).CreateDocumentAsync("SOP-EM-001", "Test", Ct);
        }

        CurrentUser.Permissions = AuthorPermissions
            .Where(p => p != PermissionNames.DocumentEditDraft)
            .ToList();

        await using var scope2 = NewScope();
        Func<Task> act = async () => await Lifecycle(scope2).EditDraftMetadataAsync(
            doc.Id, "X", "Y", Ct);

        var ex = (await act.Should().ThrowAsync<UnauthorizedOperationException>()).Subject.Single();
        ex.PermissionName.Should().Be(PermissionNames.DocumentEditDraft);
    }

    [Fact]
    public async Task EditMetadata_DocumentNotFound_ThrowsKeyNotFoundAsync()
    {
        BecomeAuthor(Guid.NewGuid());

        await using var scope = NewScope();
        Func<Task> act = async () => await Lifecycle(scope).EditDraftMetadataAsync(
            Guid.NewGuid(), "X", "Y", Ct);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task EditMetadata_LatestRevisionPastDraft_ThrowsInvalidOperationAsync()
    {
        var authorId = Guid.NewGuid();
        BecomeAuthor(authorId);

        var docId = Guid.NewGuid();
        var revisionId = Guid.NewGuid();
        var sigId = Guid.NewGuid();

        // Plant a Document whose only revision is past Draft.
        await using (var ctx = NewContext())
        {
            ctx.Documents.Add(new Document(docId, "SOP-PD-001", "Past Draft"));
            ctx.Signatures.Add(new EasySynQ.Domain.Entities.Audit.Signature(
                id: sigId,
                utcTimestamp: Clock.UtcNow,
                roleAtTimeOfSign: AuthorRole,
                signedEntityType: nameof(DocumentRevision),
                signedEntityId: revisionId.ToString(),
                payloadHash: new string('a', 64)));
            var rev = new DocumentRevision(revisionId, docId, "Rev A", authorId);
            rev.Submit(sigId, Clock.UtcNow);
            ctx.DocumentRevisions.Add(rev);
            await ctx.SaveChangesAsync(Ct);
        }

        await using var scope = NewScope();
        Func<Task> act = async () => await Lifecycle(scope).EditDraftMetadataAsync(
            docId, "X", "Y", Ct);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*expected 'Draft'*");
    }

    // ─── HardDeleteDraftAsync ───────────────────────────────────────

    [Fact]
    public async Task HardDelete_HappyPath_RemovesBothRowsAndWritesAuditAsync()
    {
        var authorId = Guid.NewGuid();
        BecomeAuthor(authorId);

        Document doc;
        await using (var scope = NewScope())
        {
            doc = await Lifecycle(scope).CreateDocumentAsync("SOP-HD-001", "Hard delete test", Ct);
        }

        var corr = Guid.NewGuid();
        Correlation.CurrentCorrelationId = corr;

        await using (var scope = NewScope())
        {
            await Lifecycle(scope).HardDeleteDraftAsync(doc.Id, Ct);
        }

        await using (var ctx = NewContext())
        {
            // Both operational rows are gone (even when bypassing the
            // soft-delete filter, no row remains).
            var docStillThere = await ctx.Documents
                .IgnoreQueryFilters()
                .AnyAsync(d => d.Id == doc.Id, Ct);
            docStillThere.Should().BeFalse();

            var revStillThere = await ctx.DocumentRevisions
                .IgnoreQueryFilters()
                .AnyAsync(r => r.DocumentId == doc.Id, Ct);
            revStillThere.Should().BeFalse();
        }

        // Audit row count = 2 (Document HardDelete + DocumentRevision
        // HardDelete), both sharing CorrelationId.
        var rows = await AuditRowsByCorrelationAsync(corr);
        rows.Count.Should().Be(2);
        rows.Should().OnlyContain(r => r.Action == AuditAction.HardDelete);
        rows.Should().OnlyContain(r => r.CorrelationId == corr);

        // Both audit rows preserve the full pre-delete JSON snapshot
        // in the "before" field per ADR 0002.
        rows.Should().OnlyContain(r => !string.IsNullOrWhiteSpace(r.Before));
        rows.Should().OnlyContain(r => r.After == null);

        // The pair covers the Document and the DocumentRevision.
        rows.Select(r => r.EntityTypeName).Should().BeEquivalentTo(
            [nameof(Document), nameof(DocumentRevision)]);
    }

    [Fact]
    public async Task HardDelete_MissingPermission_ThrowsAsync()
    {
        var authorId = Guid.NewGuid();
        BecomeAuthor(authorId);

        Document doc;
        await using (var scope = NewScope())
        {
            doc = await Lifecycle(scope).CreateDocumentAsync("SOP-HD-002", "Perm test", Ct);
        }

        CurrentUser.Permissions = AuthorPermissions
            .Where(p => p != PermissionNames.DocumentHardDelete)
            .ToList();

        await using var scope2 = NewScope();
        Func<Task> act = async () => await Lifecycle(scope2).HardDeleteDraftAsync(doc.Id, Ct);

        var ex = (await act.Should().ThrowAsync<UnauthorizedOperationException>()).Subject.Single();
        ex.PermissionName.Should().Be(PermissionNames.DocumentHardDelete);
    }

    [Fact]
    public async Task HardDelete_NotAuthor_ThrowsInvalidOperationAsync()
    {
        var authorId = Guid.NewGuid();
        BecomeAuthor(authorId);

        Document doc;
        await using (var scope = NewScope())
        {
            doc = await Lifecycle(scope).CreateDocumentAsync("SOP-HD-003", "Author-only test", Ct);
        }

        // Switch to a different user holding the same permission set.
        var otherUserId = Guid.NewGuid();
        BecomeAuthor(otherUserId);

        await using var scope2 = NewScope();
        Func<Task> act = async () => await Lifecycle(scope2).HardDeleteDraftAsync(doc.Id, Ct);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*is not the author*");
    }

    [Fact]
    public async Task HardDelete_RevisionPastDraft_ThrowsInvalidOperationAsync()
    {
        var authorId = Guid.NewGuid();
        BecomeAuthor(authorId);

        var docId = Guid.NewGuid();
        var revisionId = Guid.NewGuid();
        var sigId = Guid.NewGuid();

        await using (var ctx = NewContext())
        {
            ctx.Documents.Add(new Document(docId, "SOP-HD-PAST", "Past Draft"));
            ctx.Signatures.Add(new EasySynQ.Domain.Entities.Audit.Signature(
                id: sigId,
                utcTimestamp: Clock.UtcNow,
                roleAtTimeOfSign: AuthorRole,
                signedEntityType: nameof(DocumentRevision),
                signedEntityId: revisionId.ToString(),
                payloadHash: new string('a', 64)));
            var rev = new DocumentRevision(revisionId, docId, "Rev A", authorId);
            rev.Submit(sigId, Clock.UtcNow);
            ctx.DocumentRevisions.Add(rev);
            await ctx.SaveChangesAsync(Ct);
        }

        await using var scope = NewScope();
        Func<Task> act = async () => await Lifecycle(scope).HardDeleteDraftAsync(docId, Ct);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*expected 'Draft'*");
    }

    [Fact]
    public async Task HardDelete_MultiRevisionDocument_ThrowsInvalidOperationAsync()
    {
        var authorId = Guid.NewGuid();
        BecomeAuthor(authorId);

        // Plant a Document with two Draft revisions to exercise the
        // count-guard. The C6a happy path won't ever produce a multi-
        // revision document, but the guard is the defensive shield for
        // future-scope safety.
        var docId = Guid.NewGuid();
        await using (var ctx = NewContext())
        {
            ctx.Documents.Add(new Document(docId, "SOP-HD-MULTI", "Multi-rev test"));
            ctx.DocumentRevisions.Add(new DocumentRevision(Guid.NewGuid(), docId, "Rev A", authorId));
            ctx.DocumentRevisions.Add(new DocumentRevision(Guid.NewGuid(), docId, "Rev B", authorId));
            await ctx.SaveChangesAsync(Ct);
        }

        await using var scope = NewScope();
        Func<Task> act = async () => await Lifecycle(scope).HardDeleteDraftAsync(docId, Ct);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*expected exactly 1 revision*");
    }

    [Fact]
    public async Task HardDelete_DocumentNotFound_ThrowsKeyNotFoundAsync()
    {
        BecomeAuthor(Guid.NewGuid());

        await using var scope = NewScope();
        Func<Task> act = async () => await Lifecycle(scope).HardDeleteDraftAsync(Guid.NewGuid(), Ct);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
