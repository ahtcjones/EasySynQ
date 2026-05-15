using System.Diagnostics.CodeAnalysis;

namespace EasySynQ.Domain.Enums;

/// <summary>
/// State of a single reviewer's assignment on a single
/// <c>DocumentRevision</c> (ADR 0008). Stored as TEXT in the database
/// per the same readability rationale as <see cref="DocumentLifecycle"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Discarded preserves the audit trail.</b> When a revision returns
/// from In Review to Draft mid-review, in-progress assignments
/// transition to <see cref="Discarded"/> rather than being deleted —
/// the row remains in the table so an auditor can reconstruct who was
/// asked to review what and at what point the review cycle reset. The
/// corresponding <c>Signature</c> rows (if a reviewer had already
/// signed before the return-to-Draft) are also preserved; they just
/// no longer count toward approval.
/// </para>
/// </remarks>
[SuppressMessage(
    "Naming",
    "CA1720:Identifiers should not contain type names",
    Justification = "ADR 0008 prescribes 'Signed' as the enum value name. The analyzer flags the resemblance to the C++ 'signed' keyword; here it is a past-participle adjective describing the assignment state, not a type reference. Renaming would diverge from the ADR's domain language.")]
public enum DocumentReviewAssignmentStatus
{
    /// <summary>Reviewer assigned; has not yet signed.</summary>
    Pending,

    /// <summary>Reviewer has signed; signature reference is populated.</summary>
    Signed,

    /// <summary>
    /// Assignment was discarded because the revision returned to Draft
    /// before all reviewer signatures landed. Preserved for audit; does
    /// not count toward approval. Any signature row that already existed
    /// when this assignment was discarded is also preserved.
    /// </summary>
    Discarded,
}
