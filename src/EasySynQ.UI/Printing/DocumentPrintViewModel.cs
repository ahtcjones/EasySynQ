using EasySynQ.Domain.Entities.Audit;
using EasySynQ.Domain.Entities.Documents;
using EasySynQ.Domain.Enums;

namespace EasySynQ.UI.Printing;

/// <summary>
/// Snapshot DTO assembled by <see cref="DocumentPrintService"/> at
/// print-time. Carries every value
/// <see cref="DocumentPrintBuilder.BuildFlowDocument"/> needs to render
/// the print layout — the builder is a pure function over this DTO,
/// which keeps the FlowDocument construction unit-testable independent
/// of repository plumbing (ADR 0008 C7 / SPEC §4.5).
/// </summary>
/// <remarks>
/// <para>
/// <b>Snapshot, not live.</b> The DTO reflects the database state at
/// the moment <see cref="DocumentPrintService"/> assembled it.
/// Subsequent mutations to the underlying entities do not propagate
/// into a previously-built FlowDocument. <see cref="SnapshotTimestampUtc"/>
/// is the timestamp the print job carries in its footer.
/// </para>
/// <para>
/// <b>Author + reviewers joined through signature rows.</b> Per ADR
/// 0008's signed-payload contract,
/// <see cref="DocumentReviewAssignment.SignatureId"/> points at the
/// reviewer's <see cref="Signature"/> row; the
/// <see cref="DocumentRevision.AuthorSignatureId"/> points at the
/// author's submission signature. Both signatures are pre-fetched here
/// so the builder does not re-query during rendering.
/// </para>
/// </remarks>
public sealed record DocumentPrintViewModel(
    Document Document,
    DocumentRevision CurrentRevision,
    string CurrentRevisionLifecycleDisplay,
    string AuthorDisplay,
    Signature? AuthorSignature,
    IReadOnlyList<ReviewerPrintRow> Reviewers,
    IReadOnlyList<DocumentReviewComment> Comments,
    IReadOnlyList<AuditLogEntry> AuditTrail,
    IReadOnlyDictionary<Guid, string> UserDisplayLookup,
    DateTime SnapshotTimestampUtc);

/// <summary>
/// Per-reviewer projection. Resolves the reviewer's display name and
/// (when signed) the signature's role + timestamp + payload hash so the
/// builder's signature block can render the audit-anchor fields
/// (SPEC §3.4) without re-querying.
/// </summary>
/// <param name="ReviewerUserId">Soft-FK reference to the reviewer's
/// <c>User</c> row.</param>
/// <param name="ReviewerDisplay">Display name resolved at snapshot time
/// (falls back to <c>"(unknown)"</c> when the row is missing or
/// soft-deleted).</param>
/// <param name="Status">Per-assignment lifecycle state — Pending /
/// Signed / Discarded.</param>
/// <param name="AssignedAtUtc">UTC instant the reviewer was assigned.</param>
/// <param name="SignedAtUtc">UTC instant the reviewer signed, or
/// <see langword="null"/> when not yet Signed.</param>
/// <param name="Signature">The reviewer's signature row, or
/// <see langword="null"/> when not Signed.</param>
public sealed record ReviewerPrintRow(
    Guid ReviewerUserId,
    string ReviewerDisplay,
    DocumentReviewAssignmentStatus Status,
    DateTime AssignedAtUtc,
    DateTime? SignedAtUtc,
    Signature? Signature);
