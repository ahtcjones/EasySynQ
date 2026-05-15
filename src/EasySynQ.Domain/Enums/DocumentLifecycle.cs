namespace EasySynQ.Domain.Enums;

/// <summary>
/// Lifecycle state for a controlled document revision (SPEC §5.1
/// amended by ADR 0008). Stored as TEXT in the database (per EF Core
/// configuration) so raw queries are readable; the trade-off is
/// 2× storage vs. the integer backing, which is negligible relative
/// to the surrounding revision row size.
/// </summary>
/// <remarks>
/// <para>
/// <b>Active is not stored.</b> The "Active" sub-state of Approved is
/// derived at read time from <c>ApprovedAtUtc</c> + <c>EffectiveFromUtc</c>
/// + the presence of a later superseding revision. A revision in this
/// enum's <see cref="Approved"/> state may or may not be Active at any
/// given instant; query callers compute that. The "stored status that
/// lies because nobody updated it" failure mode is avoided by not
/// storing it.
/// </para>
/// <para>
/// <b>Transitions</b> are enforced by the service layer (lifecycle
/// service in C3), not by database constraints. Service validates the
/// current state allows the requested transition; database holds
/// whatever the service writes.
/// </para>
/// </remarks>
public enum DocumentLifecycle
{
    /// <summary>Author edits freely. May be hard-deleted by author (no signatures yet).</summary>
    Draft,

    /// <summary>
    /// Reviewers assigned; author cannot edit. Each assigned reviewer
    /// signs to advance the document. Return-to-Draft discards
    /// in-progress reviewer signatures.
    /// </summary>
    InReview,

    /// <summary>
    /// All reviewer signatures captured. Document signed off, but not
    /// necessarily in effect yet — the "Active" sub-state is derived
    /// from <c>ApprovedAtUtc</c> + <c>EffectiveFromUtc</c>.
    /// </summary>
    Approved,

    /// <summary>A new revision has been approved and made Active in this revision's place.</summary>
    Superseded,

    /// <summary>The document has been retired with no successor.</summary>
    Archived,
}
