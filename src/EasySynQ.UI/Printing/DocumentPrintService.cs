using System.Globalization;
using System.Windows.Controls;
using System.Windows.Documents;

using EasySynQ.Domain;
using EasySynQ.Domain.Entities.Audit;
using EasySynQ.Domain.Entities.Documents;
using EasySynQ.Domain.Entities.Identity;
using EasySynQ.Domain.Enums;
using EasySynQ.Services.Abstractions;
using EasySynQ.Services.Time;

using Microsoft.EntityFrameworkCore;

namespace EasySynQ.UI.Printing;

/// <summary>
/// Production <see cref="IDocumentPrintService"/>. Re-fetches the
/// Document + latest revision + signatures + reviewer assignments +
/// reviewer comments + audit trail at print-time; assembles a
/// <see cref="DocumentPrintViewModel"/>; calls
/// <see cref="DocumentPrintBuilder.BuildFlowDocument"/>; surfaces a
/// <see cref="PrintDialog"/> modally (ADR 0008 C7 / SPEC §4.5).
/// </summary>
/// <remarks>
/// <para>
/// <b>Lifecycle helper for the Print affordance.</b> The Document
/// detail VM injects this service and exposes a
/// <c>PrintCommand</c> that calls
/// <see cref="PrintAsync"/> with the bound document's id. No
/// permission is gated — printing is always available to anyone who
/// can see the document (no <c>Document.Print</c> entry in the ADR
/// 0007 catalog).
/// </para>
/// <para>
/// <b>Captive scoped dependencies.</b> The service is registered as a
/// singleton matching the project-wide prompter pattern; the
/// scoped repository dependencies are captured at first resolution
/// and shared across calls. This mirrors how the existing prompters
/// work and is acceptable for the WPF single-user host.
/// </para>
/// </remarks>
public sealed class DocumentPrintService : IDocumentPrintService
{
    private readonly IDocumentRepository _documents;
    private readonly IDocumentRevisionRepository _revisions;
    private readonly IDocumentReviewAssignmentRepository _assignments;
    private readonly IDocumentReviewCommentRepository _comments;
    private readonly IUserRepository _users;
    private readonly IRepository<Signature, Guid> _signatures;
    private readonly IAuditLogRepository _auditLog;
    private readonly IClock _clock;

    /// <summary>Constructs the service over its scoped repository deps.</summary>
    public DocumentPrintService(
        IDocumentRepository documents,
        IDocumentRevisionRepository revisions,
        IDocumentReviewAssignmentRepository assignments,
        IDocumentReviewCommentRepository comments,
        IUserRepository users,
        IRepository<Signature, Guid> signatures,
        IAuditLogRepository auditLog,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(revisions);
        ArgumentNullException.ThrowIfNull(assignments);
        ArgumentNullException.ThrowIfNull(comments);
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(signatures);
        ArgumentNullException.ThrowIfNull(auditLog);
        ArgumentNullException.ThrowIfNull(clock);

        _documents = documents;
        _revisions = revisions;
        _assignments = assignments;
        _comments = comments;
        _users = users;
        _signatures = signatures;
        _auditLog = auditLog;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task PrintAsync(Guid documentId, CancellationToken cancellationToken)
    {
        if (documentId == Guid.Empty)
        {
            throw new ArgumentException("DocumentId must not be Guid.Empty.", nameof(documentId));
        }

        var vm = await AssembleAsync(documentId, cancellationToken);
        if (vm is null)
        {
            // Document was deleted between the user clicking Print and
            // the service running. No-op; the calling VM should refresh
            // its list. Surfacing an exception here would land in the
            // shell's exception handler — silent skip is the safer
            // pattern for "user pressed Print on a row that no longer
            // exists" in a multi-user pilot.
            return;
        }

        var flow = DocumentPrintBuilder.BuildFlowDocument(vm);

        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        // FlowDocument's DocumentPaginator honors the FlowDocument's
        // configured PageWidth / PageHeight when not overridden by the
        // PrintDialog's PrintableArea — but defensively re-pin so a
        // user-selected page size that PrintDialog reports does not
        // collapse the printed layout. US Letter is the project's
        // print target per CLAUDE.md.
        var paginator = ((IDocumentPaginatorSource)flow).DocumentPaginator;
        flow.PageWidth = dialog.PrintableAreaWidth;
        flow.PageHeight = dialog.PrintableAreaHeight;
        dialog.PrintDocument(paginator, $"EasySynQ — {vm.Document.Number}");
    }

    /// <summary>
    /// Internal — exposed at <see langword="internal"/> visibility so
    /// the test project (via InternalsVisibleTo, where present) can
    /// exercise the assembly path without driving the WPF print
    /// dialog. The orchestration is the testable shape; PrintDialog
    /// itself is BCL surface.
    /// </summary>
    internal async Task<DocumentPrintViewModel?> AssembleAsync(
        Guid documentId,
        CancellationToken cancellationToken)
    {
        var document = await _documents.GetByIdAsync(documentId, cancellationToken);
        if (document is null) return null;

        var currentRevision = await _revisions.GetLatestRevisionAsync(documentId, cancellationToken);
        if (currentRevision is null) return null;

        var assignments = await _assignments.GetByRevisionIdAsync(currentRevision.Id, cancellationToken);
        var comments = await _comments.GetByRevisionIdAsync(currentRevision.Id, cancellationToken);

        // Resolve display names for everyone implicated by the
        // snapshot (author + assigned reviewers + comment authors).
        // Soft-FKs per ADR 0004 — soft-deleted user rows surface as
        // "(unknown)" in the rendered print.
        var userIds = new HashSet<Guid>
        {
            currentRevision.AuthorUserId,
        };
        foreach (var a in assignments) userIds.Add(a.ReviewerUserId);
        foreach (var c in comments) userIds.Add(c.AuthorUserId);
        var users = await _users.GetByIdsAsync(userIds, cancellationToken);
        var displayLookup = users.ToDictionary(u => u.Id, FormatUserDisplay);

        // Pre-fetch signatures referenced by the author + signed
        // reviewer assignments so the builder does not re-query.
        var signatureIds = new HashSet<Guid>();
        if (currentRevision.AuthorSignatureId is { } sigId) signatureIds.Add(sigId);
        foreach (var a in assignments)
        {
            if (a.SignatureId is { } id) signatureIds.Add(id);
        }
        var signatureLookup = new Dictionary<Guid, Signature>();
        foreach (var id in signatureIds)
        {
            var s = await _signatures.GetByIdAsync(id, cancellationToken);
            if (s is not null) signatureLookup[id] = s;
        }

        var authorSignature = currentRevision.AuthorSignatureId is { } authorSigId
            ? signatureLookup.GetValueOrDefault(authorSigId)
            : null;

        var reviewerRows = assignments
            .Select(a => new ReviewerPrintRow(
                ReviewerUserId: a.ReviewerUserId,
                ReviewerDisplay: displayLookup.TryGetValue(a.ReviewerUserId, out var d) ? d : "(unknown)",
                Status: a.Status,
                AssignedAtUtc: a.AssignedAtUtc,
                SignedAtUtc: a.SignedAtUtc,
                Signature: a.SignatureId is { } id ? signatureLookup.GetValueOrDefault(id) : null))
            .ToList();

        // Audit trail spans the Document AND every revision row —
        // auditors think per-document, not per-revision. Chronological
        // across both.
        var docAuditRows = await _auditLog
            .ByEntity(nameof(Document), document.Id.ToString("D", CultureInfo.InvariantCulture))
            .ToListAsync(cancellationToken);
        var revAuditRows = await _auditLog
            .ByEntity(nameof(DocumentRevision), currentRevision.Id.ToString("D", CultureInfo.InvariantCulture))
            .ToListAsync(cancellationToken);
        var auditTrail = docAuditRows.Concat(revAuditRows)
            .OrderBy(a => a.UtcTimestamp)
            .ToList();

        var authorDisplay = displayLookup.TryGetValue(currentRevision.AuthorUserId, out var ad)
            ? ad
            : "(unknown)";

        return new DocumentPrintViewModel(
            Document: document,
            CurrentRevision: currentRevision,
            CurrentRevisionLifecycleDisplay: FormatLifecycle(currentRevision.Lifecycle),
            AuthorDisplay: authorDisplay,
            AuthorSignature: authorSignature,
            Reviewers: reviewerRows,
            Comments: comments,
            AuditTrail: auditTrail,
            UserDisplayLookup: displayLookup,
            SnapshotTimestampUtc: _clock.UtcNow);
    }

    private static string FormatUserDisplay(User u)
        => string.IsNullOrWhiteSpace(u.DisplayName) ? u.Username : u.DisplayName;

    private static string FormatLifecycle(DocumentLifecycle lifecycle)
        => lifecycle switch
        {
            DocumentLifecycle.Draft => "Draft",
            DocumentLifecycle.InReview => "In Review",
            DocumentLifecycle.Approved => "Approved",
            DocumentLifecycle.Superseded => "Superseded",
            DocumentLifecycle.Archived => "Archived",
            _ => lifecycle.ToString(),
        };
}
