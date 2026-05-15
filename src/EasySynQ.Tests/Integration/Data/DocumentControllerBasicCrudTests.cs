using AwesomeAssertions;

using EasySynQ.Domain.Entities.Documents;
using EasySynQ.Domain.Enums;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Xunit;

namespace EasySynQ.Tests.Integration.Data;

/// <summary>
/// Basic CRUD round-trips for the seven Phase 2 Document Controller
/// entities (ADR 0008). Each test inserts an entity, reads it back via
/// a fresh DbContext, and asserts the field values survive
/// serialization to SQLite and back. The shape exercises:
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>EF Core configurations map every property correctly
///   (column names, types, lengths, enum string conversions).</item>
///   <item>Within-aggregate FKs accept valid foreign-key references.</item>
///   <item>Soft Guid references store and round-trip without DB-level
///   constraint enforcement (ADR 0004).</item>
///   <item>Standard auditable fields (CreatedBy/CreatedUtc/etc.)
///   round-trip in their default-empty state — the interceptor
///   pipeline is exercised by a separate test class; this one just
///   verifies the schema is shaped right.</item>
/// </list>
/// <para>
/// Service-layer lifecycle behavior (state transitions, signatures,
/// reviewer-discard semantics, retraining cascade) lands with
/// Phase 2 C3 and gets its own test files. This file pins schema
/// correctness only.
/// </para>
/// </remarks>
public class DocumentControllerBasicCrudTests : IntegrationTestBase
{
    private const string ValidHash =
        "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    private static readonly DateTime FixedUtc =
        new(2026, 5, 14, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Document_RoundTripsAsync()
    {
        var id = Guid.NewGuid();
        var doc = new Document(id, "SOP-Q-001", "Quality entry SOP");

        await using (var ctx = NewContext())
        {
            ctx.Documents.Add(doc);
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            var loaded = await ctx.Documents.SingleAsync(d => d.Id == id, Ct);
            loaded.Number.Should().Be("SOP-Q-001");
            loaded.Title.Should().Be("Quality entry SOP");
            loaded.RetiredAtUtc.Should().BeNull();
            loaded.RetiredByUserId.Should().BeNull();
            loaded.RetirementSignatureId.Should().BeNull();
        }
    }

    [Fact]
    public async Task DocumentRevision_RoundTripsAsync()
    {
        var doc = new Document(Guid.NewGuid(), "SOP-Q-002", "Furnace SOP");
        var revId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var rev = new DocumentRevision(revId, doc.Id, "Rev A", authorId);

        await using (var ctx = NewContext())
        {
            ctx.Documents.Add(doc);
            ctx.DocumentRevisions.Add(rev);
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            var loaded = await ctx.DocumentRevisions.SingleAsync(r => r.Id == revId, Ct);
            loaded.DocumentId.Should().Be(doc.Id);
            loaded.RevisionLabel.Should().Be("Rev A");
            loaded.Lifecycle.Should().Be(DocumentLifecycle.Draft);
            loaded.AuthorUserId.Should().Be(authorId);
            loaded.EffectiveFromUtc.Should().BeNull();
            loaded.ApprovedAtUtc.Should().BeNull();
            loaded.VaultBlobId.Should().BeNull();
            loaded.AuthorSignatureId.Should().BeNull();
            loaded.LockedAtUtc.Should().BeNull();
        }
    }

    [Fact]
    public async Task DocumentRevision_LifecycleEnum_StoredAsTextAsync()
    {
        // Pin the EF configuration's HasConversion<string>() — the
        // Lifecycle column should hold the enum's name, not its
        // integer value. Verified by raw SQL against the underlying
        // SQLite connection so the assertion observes the stored
        // form directly, not EF's read-time conversion.
        var doc = new Document(Guid.NewGuid(), "SOP-Q-003", "Enum storage check");
        var rev = new DocumentRevision(Guid.NewGuid(), doc.Id, "Rev A", Guid.NewGuid());

        await using (var ctx = NewContext())
        {
            ctx.Documents.Add(doc);
            ctx.DocumentRevisions.Add(rev);
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            var conn = (SqliteConnection)ctx.Database.GetDbConnection();
            await conn.OpenAsync(Ct);
            try
            {
                // Query the only row in the table — no parameter
                // binding needed and no GUID-encoding gotchas. The
                // assertion is on the Lifecycle column's stored form,
                // not on row selection.
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT Lifecycle FROM DocumentRevisions;";
                var raw = (string?)await cmd.ExecuteScalarAsync(Ct);
                raw.Should().Be("Draft");
            }
            finally
            {
                await conn.CloseAsync();
            }
        }
    }

    [Fact]
    public async Task ExternalDocument_RoundTripsAsync()
    {
        var id = Guid.NewGuid();
        var ext = new ExternalDocument(id, "ASTM", "ASTM A29", "2024", FixedUtc);

        await using (var ctx = NewContext())
        {
            ctx.ExternalDocuments.Add(ext);
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            var loaded = await ctx.ExternalDocuments.SingleAsync(e => e.Id == id, Ct);
            loaded.IssuingBody.Should().Be("ASTM");
            loaded.Designation.Should().Be("ASTM A29");
            loaded.CurrentRevisionLabel.Should().Be("2024");
            loaded.CurrentEffectiveDateUtc.Should().Be(FixedUtc);
        }
    }

    [Fact]
    public async Task DocumentLink_RoundTripsAsync()
    {
        var doc = new Document(Guid.NewGuid(), "SOP-Q-004", "Linked SOP");
        var rev = new DocumentRevision(Guid.NewGuid(), doc.Id, "Rev A", Guid.NewGuid());
        var ext = new ExternalDocument(Guid.NewGuid(), "AMS", "AMS 2750G", "G", null);
        var link = new DocumentLink(Guid.NewGuid(), rev.Id, ext.Id);

        await using (var ctx = NewContext())
        {
            ctx.Documents.Add(doc);
            ctx.DocumentRevisions.Add(rev);
            ctx.ExternalDocuments.Add(ext);
            ctx.DocumentLinks.Add(link);
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            var loaded = await ctx.DocumentLinks.SingleAsync(l => l.Id == link.Id, Ct);
            loaded.DocumentRevisionId.Should().Be(rev.Id);
            loaded.ExternalDocumentId.Should().Be(ext.Id);
            loaded.CompatibilityReviewRequiredFlag.Should().BeFalse();
        }
    }

    [Fact]
    public async Task DocumentReviewAssignment_RoundTripsAsync()
    {
        var doc = new Document(Guid.NewGuid(), "SOP-Q-005", "Review-assignment host");
        var rev = new DocumentRevision(Guid.NewGuid(), doc.Id, "Rev A", Guid.NewGuid());
        var assignmentId = Guid.NewGuid();
        var reviewerId = Guid.NewGuid();
        var assignedById = Guid.NewGuid();
        var assignment = new DocumentReviewAssignment(
            assignmentId, rev.Id, reviewerId, FixedUtc, assignedById);

        await using (var ctx = NewContext())
        {
            ctx.Documents.Add(doc);
            ctx.DocumentRevisions.Add(rev);
            ctx.DocumentReviewAssignments.Add(assignment);
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            var loaded = await ctx.DocumentReviewAssignments.SingleAsync(
                a => a.Id == assignmentId, Ct);
            loaded.DocumentRevisionId.Should().Be(rev.Id);
            loaded.ReviewerUserId.Should().Be(reviewerId);
            loaded.AssignedAtUtc.Should().Be(FixedUtc);
            loaded.AssignedByUserId.Should().Be(assignedById);
            loaded.Status.Should().Be(DocumentReviewAssignmentStatus.Pending);
            loaded.SignedAtUtc.Should().BeNull();
            loaded.SignatureId.Should().BeNull();
        }
    }

    [Fact]
    public async Task DocumentReviewComment_RoundTripsAsync()
    {
        var doc = new Document(Guid.NewGuid(), "SOP-Q-006", "Commentable revision host");
        var rev = new DocumentRevision(Guid.NewGuid(), doc.Id, "Rev A", Guid.NewGuid());
        var commentId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var comment = new DocumentReviewComment(
            commentId, rev.Id, authorId, "Clarify Figure 3.", FixedUtc);

        await using (var ctx = NewContext())
        {
            ctx.Documents.Add(doc);
            ctx.DocumentRevisions.Add(rev);
            ctx.DocumentReviewComments.Add(comment);
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            var loaded = await ctx.DocumentReviewComments.SingleAsync(c => c.Id == commentId, Ct);
            loaded.DocumentRevisionId.Should().Be(rev.Id);
            loaded.AuthorUserId.Should().Be(authorId);
            loaded.BodyText.Should().Be("Clarify Figure 3.");
            loaded.CreatedAtUtc.Should().Be(FixedUtc);
        }
    }

    [Fact]
    public async Task VaultBlob_RoundTripsAsync()
    {
        var id = Guid.NewGuid();
        var blob = new VaultBlob(
            id, ValidHash, 4096, "application/pdf", "manual.pdf", FixedUtc);

        await using (var ctx = NewContext())
        {
            ctx.VaultBlobs.Add(blob);
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            var loaded = await ctx.VaultBlobs.SingleAsync(b => b.Id == id, Ct);
            loaded.Sha256Hash.Should().Be(ValidHash);
            loaded.FileSizeBytes.Should().Be(4096);
            loaded.MimeType.Should().Be("application/pdf");
            loaded.OriginalFileName.Should().Be("manual.pdf");
            loaded.StoredAtUtc.Should().Be(FixedUtc);
        }
    }

    [Fact]
    public async Task Document_Number_UniqueIndexEnforcesAsync()
    {
        // Phase 2 catalog discipline: Number is org-assigned and
        // unique within the deployment. The configuration declares a
        // unique index on Number; a duplicate insert must fail.
        var first = new Document(Guid.NewGuid(), "SOP-DUP-001", "First");
        var second = new Document(Guid.NewGuid(), "SOP-DUP-001", "Second");

        await using (var ctx = NewContext())
        {
            ctx.Documents.Add(first);
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            ctx.Documents.Add(second);
            var act = async () => await ctx.SaveChangesAsync(Ct);
            (await act.Should().ThrowAsync<DbUpdateException>())
                .WithInnerException<Microsoft.Data.Sqlite.SqliteException>()
                .Where(ex => ex.Message.Contains("UNIQUE", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task VaultBlob_Sha256Hash_UniqueIndexEnforcesAsync()
    {
        // Content addressing requires hash uniqueness — the dedup
        // mechanism. Two blobs with the same hash must fail.
        var first = new VaultBlob(
            Guid.NewGuid(), ValidHash, 1, "application/pdf", "first.pdf", FixedUtc);
        var second = new VaultBlob(
            Guid.NewGuid(), ValidHash, 1, "application/pdf", "second.pdf", FixedUtc);

        await using (var ctx = NewContext())
        {
            ctx.VaultBlobs.Add(first);
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            ctx.VaultBlobs.Add(second);
            var act = async () => await ctx.SaveChangesAsync(Ct);
            (await act.Should().ThrowAsync<DbUpdateException>())
                .WithInnerException<Microsoft.Data.Sqlite.SqliteException>()
                .Where(ex => ex.Message.Contains("UNIQUE", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task ExternalDocument_IssuingBodyDesignation_UniqueIndexEnforcesAsync()
    {
        // ASTM A29 should not appear twice in the catalog. The
        // composite unique index on (IssuingBody, Designation)
        // enforces it at the schema level.
        var first = new ExternalDocument(Guid.NewGuid(), "ASTM", "A29", "2024", null);
        var second = new ExternalDocument(Guid.NewGuid(), "ASTM", "A29", "2025", null);

        await using (var ctx = NewContext())
        {
            ctx.ExternalDocuments.Add(first);
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            ctx.ExternalDocuments.Add(second);
            var act = async () => await ctx.SaveChangesAsync(Ct);
            (await act.Should().ThrowAsync<DbUpdateException>())
                .WithInnerException<Microsoft.Data.Sqlite.SqliteException>()
                .Where(ex => ex.Message.Contains("UNIQUE", StringComparison.Ordinal));
        }
    }
}
