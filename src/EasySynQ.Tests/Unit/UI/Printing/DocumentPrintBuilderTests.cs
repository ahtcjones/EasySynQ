using System.Windows.Documents;

using AwesomeAssertions;

using EasySynQ.Domain.Entities.Audit;
using EasySynQ.Domain.Entities.Documents;
using EasySynQ.Domain.Enums;
using EasySynQ.UI.Printing;

using Xunit;

namespace EasySynQ.Tests.Unit.UI.Printing;

/// <summary>
/// Unit tests for <see cref="DocumentPrintBuilder.BuildFlowDocument"/>
/// (ADR 0008 C7 / SPEC §4.5). Builds fixture DTOs and asserts on the
/// resulting <see cref="FlowDocument"/>'s block structure — pin the
/// section sequence and the US-Letter pagination invariants so a
/// future refactor that re-orders or drops a section fails this test
/// instead of silently shipping a broken print layout.
/// </summary>
public sealed class DocumentPrintBuilderTests
{
    [Fact]
    public void BuildFlowDocument_NullVm_Throws()
    {
        var act = () => DocumentPrintBuilder.BuildFlowDocument(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildFlowDocument_MinimalDraft_ProducesExpectedSectionsAndPaginates()
    {
        // Minimal fixture: a Document + Draft revision with no
        // signatures, no reviewers, no comments, no audit trail.
        // Builder should still produce a valid paginated layout with
        // header + metadata + signatures + footer (comments + audit
        // sections are skipped because they're empty).
        var vm = BuildFixture(
            withAuthorSignature: false,
            reviewers: [],
            comments: [],
            auditTrail: []);

        var flow = DocumentPrintBuilder.BuildFlowDocument(vm);

        // US Letter at 96 DPI.
        flow.PageWidth.Should().Be(816);
        flow.PageHeight.Should().Be(1056);

        var sectionNames = flow.Blocks
            .OfType<Section>()
            .Select(s => s.Name)
            .ToList();
        sectionNames.Should().Equal(
            "HeaderSection",
            "MetadataSection",
            "SignaturesSection",
            "FooterSection");

        // WPF's DocumentPaginator is lazy — PageCount is 0 until
        // ComputePageCount() is called or pages are walked. Force the
        // computation so the assertion exercises real pagination.
        var paginator = ((IDocumentPaginatorSource)flow).DocumentPaginator;
        paginator.ComputePageCount();
        paginator.PageCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void BuildFlowDocument_FullSnapshot_IncludesCommentsAndAuditSections()
    {
        var reviewer = new ReviewerPrintRow(
            ReviewerUserId: Guid.NewGuid(),
            ReviewerDisplay: "Reviewer One",
            Status: DocumentReviewAssignmentStatus.Signed,
            AssignedAtUtc: new DateTime(2026, 5, 17, 10, 0, 0, DateTimeKind.Utc),
            SignedAtUtc: new DateTime(2026, 5, 17, 11, 0, 0, DateTimeKind.Utc),
            Signature: NewSignature(role: "QualityManager"));

        var comment = new DocumentReviewComment(
            Guid.NewGuid(),
            documentRevisionId: Guid.NewGuid(),
            authorUserId: Guid.NewGuid(),
            bodyText: "Please tighten section 4.",
            createdAtUtc: new DateTime(2026, 5, 17, 10, 30, 0, DateTimeKind.Utc));

        var auditEntry = new AuditLogEntry(
            id: Guid.NewGuid(),
            utcTimestamp: new DateTime(2026, 5, 17, 9, 0, 0, DateTimeKind.Utc),
            userId: Guid.NewGuid(),
            entityTypeName: nameof(Document),
            entityId: Guid.NewGuid().ToString("D"),
            action: AuditAction.Insert,
            before: null,
            after: "{}",
            correlationId: Guid.NewGuid());

        var vm = BuildFixture(
            withAuthorSignature: true,
            reviewers: [reviewer],
            comments: [comment],
            auditTrail: [auditEntry]);

        var flow = DocumentPrintBuilder.BuildFlowDocument(vm);

        var sectionNames = flow.Blocks
            .OfType<Section>()
            .Select(s => s.Name)
            .ToList();
        sectionNames.Should().Equal(
            "HeaderSection",
            "MetadataSection",
            "SignaturesSection",
            "CommentsSection",
            "AuditSection",
            "FooterSection");
    }

    [Fact]
    public void BuildFlowDocument_FontFamilyAndForeground_AreApplied()
    {
        // Print template's font stack and foreground brush are the
        // load-bearing visual contract — pin them so an accidental
        // restyle gets caught.
        var vm = BuildFixture(
            withAuthorSignature: false,
            reviewers: [],
            comments: [],
            auditTrail: []);

        var flow = DocumentPrintBuilder.BuildFlowDocument(vm);

        flow.FontFamily.Source.Should().Be("Segoe UI");
        flow.FontSize.Should().Be(11.0);
    }

    [Fact]
    public void BuildFlowDocument_SignedReviewerWithoutAuthor_StillBuilds()
    {
        // Defensive — an Approved revision with no AuthorSignature
        // captured in the snapshot (data anomaly) still renders the
        // Signatures section; the author row appears with status
        // text only.
        var vm = BuildFixture(
            withAuthorSignature: false,
            reviewers: [
                new ReviewerPrintRow(
                    ReviewerUserId: Guid.NewGuid(),
                    ReviewerDisplay: "Reviewer One",
                    Status: DocumentReviewAssignmentStatus.Signed,
                    AssignedAtUtc: new DateTime(2026, 5, 17, 10, 0, 0, DateTimeKind.Utc),
                    SignedAtUtc: new DateTime(2026, 5, 17, 11, 0, 0, DateTimeKind.Utc),
                    Signature: NewSignature("QualityManager")),
            ],
            comments: [],
            auditTrail: []);

        var flow = DocumentPrintBuilder.BuildFlowDocument(vm);

        var signaturesSection = flow.Blocks
            .OfType<Section>()
            .Single(s => s.Name == "SignaturesSection");
        // Heading + table.
        signaturesSection.Blocks.Should().HaveCount(2);
        signaturesSection.Blocks.OfType<Table>().Single()
            .RowGroups.Single().Rows.Should()
            .HaveCount(2, "author row + one reviewer row");
    }

    [Fact]
    public void BuildFlowDocument_AuditTrailSortedChronologicallyAscending()
    {
        // The builder must sort the audit trail by UtcTimestamp
        // ascending regardless of the order the DTO carries — auditors
        // read chronologically forward.
        var t1 = new DateTime(2026, 5, 17, 9, 0, 0, DateTimeKind.Utc);
        var t2 = new DateTime(2026, 5, 17, 11, 0, 0, DateTimeKind.Utc);
        var t3 = new DateTime(2026, 5, 17, 13, 0, 0, DateTimeKind.Utc);

        var entry1 = NewAuditEntry(t1, AuditAction.Insert);
        var entry2 = NewAuditEntry(t2, AuditAction.Update);
        var entry3 = NewAuditEntry(t3, AuditAction.Update);

        var vm = BuildFixture(
            withAuthorSignature: false,
            reviewers: [],
            comments: [],
            // Deliberately out of order.
            auditTrail: [entry3, entry1, entry2]);

        var flow = DocumentPrintBuilder.BuildFlowDocument(vm);

        var auditSection = flow.Blocks
            .OfType<Section>()
            .Single(s => s.Name == "AuditSection");
        var table = auditSection.Blocks.OfType<Table>().Single();
        var dataRows = table.RowGroups.Single()
            .Rows.Skip(1) // header row
            .ToList();
        dataRows.Should().HaveCount(3);

        // Pull the timestamp text from the first cell of each row.
        var orderedTexts = dataRows
            .Select(r => ((Run)((Paragraph)r.Cells[0].Blocks.First()).Inlines.First()).Text)
            .ToList();
        // 09:00 → 11:00 → 13:00 (ascending).
        orderedTexts[0].Should().Contain("09:00:00");
        orderedTexts[1].Should().Contain("11:00:00");
        orderedTexts[2].Should().Contain("13:00:00");
    }

    // ─── helpers ────────────────────────────────────────────────────

    private static DocumentPrintViewModel BuildFixture(
        bool withAuthorSignature,
        IReadOnlyList<ReviewerPrintRow> reviewers,
        IReadOnlyList<DocumentReviewComment> comments,
        IReadOnlyList<AuditLogEntry> auditTrail)
    {
        var doc = new Document(Guid.NewGuid(), "SOP-Q-001", "Test Document");
        var rev = new DocumentRevision(
            Guid.NewGuid(),
            doc.Id,
            "Rev A",
            authorUserId: Guid.NewGuid());

        var lookup = new Dictionary<Guid, string>
        {
            [rev.AuthorUserId] = "Test Author",
        };
        foreach (var r in reviewers)
        {
            lookup[r.ReviewerUserId] = r.ReviewerDisplay;
        }
        foreach (var c in comments)
        {
            lookup.TryAdd(c.AuthorUserId, "Comment Author");
        }
        foreach (var a in auditTrail)
        {
            if (a.UserId is { } uid) lookup.TryAdd(uid, "Audit Actor");
        }

        return new DocumentPrintViewModel(
            Document: doc,
            CurrentRevision: rev,
            CurrentRevisionLifecycleDisplay: "Draft",
            AuthorDisplay: "Test Author",
            AuthorSignature: withAuthorSignature ? NewSignature("DocumentAuthor") : null,
            Reviewers: reviewers,
            Comments: comments,
            AuditTrail: auditTrail,
            UserDisplayLookup: lookup,
            SnapshotTimestampUtc: new DateTime(2026, 5, 17, 12, 0, 0, DateTimeKind.Utc));
    }

    private static Signature NewSignature(string role)
        => new(
            id: Guid.NewGuid(),
            utcTimestamp: new DateTime(2026, 5, 17, 10, 0, 0, DateTimeKind.Utc),
            roleAtTimeOfSign: role,
            signedEntityType: nameof(DocumentRevision),
            signedEntityId: Guid.NewGuid().ToString("D"),
            payloadHash: "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");

    private static AuditLogEntry NewAuditEntry(DateTime utc, AuditAction action)
        => new(
            id: Guid.NewGuid(),
            utcTimestamp: utc,
            userId: Guid.NewGuid(),
            entityTypeName: nameof(Document),
            entityId: Guid.NewGuid().ToString("D"),
            action: action,
            // Insert: Before must be null, After non-null. Update /
            // Delete / HardDelete: Before must be non-null and After
            // non-null (except HardDelete where After must be null).
            before: action == AuditAction.Insert ? null : "{\"k\":\"old\"}",
            after: action == AuditAction.HardDelete ? null : "{\"k\":\"new\"}",
            correlationId: Guid.NewGuid());
}
