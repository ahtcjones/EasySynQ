using EasySynQ.Domain.Entities.Documents;

namespace EasySynQ.Services.Documents;

/// <summary>
/// State-machine operations on the
/// <see cref="EasySynQ.Domain.Enums.DocumentLifecycle"/> defined by ADR
/// 0008 §"Refined lifecycle state machine". Each method runs in a
/// single <c>SaveChanges</c> transaction: the state-changing entity
/// updates, any <c>Signature</c> rows being staged, the
/// <c>DocumentReviewAssignment</c> updates, and any
/// <c>DocumentRevisionApprovedEvent</c> publication all share one
/// CorrelationId via the per-save fallback established in Phase 1.
/// </summary>
/// <remarks>
/// <para>
/// <b>The "Active" sub-state is derived, not stored.</b> A revision in
/// <see cref="EasySynQ.Domain.Enums.DocumentLifecycle.Approved"/> is
/// Active when its effective date has passed AND no later revision is
/// itself currently Active. Resolution lives on
/// <c>IDocumentRevisionRepository.GetActiveRevisionAsync</c> per ADR
/// 0008 §"Effective dating on DocumentRevision".
/// </para>
/// <para>
/// <b>Approved-to-Effective gap.</b> Per ADR 0008 C3 plan §G Q9, when a
/// successor revision is approved, the prior Active revision flips to
/// <see cref="EasySynQ.Domain.Enums.DocumentLifecycle.Superseded"/>
/// immediately — regardless of the successor's
/// <c>EffectiveFromUtc</c>. During the window between the successor's
/// approval and its effective date, the document has NO currently-
/// Active revision. The as-of resolver returns
/// <see langword="null"/>; UI surfaces this as
/// "Approved (effective YYYY-MM-DD)" with no current Active highlight.
/// This is the documented and intended behavior — choosing supersede-
/// on-approval over deferred-supersede preserves the "stored Lifecycle
/// is the source of truth" invariant from ADR 0008 §"Effective dating".
/// </para>
/// </remarks>
public interface IDocumentLifecycleService
{
    /// <summary>
    /// Transitions a Draft revision to InReview. Stages the author's
    /// submission signature, creates one
    /// <see cref="DocumentReviewAssignment"/> per named reviewer (each
    /// in <c>Pending</c> status), updates the revision's
    /// <c>Lifecycle</c>, <c>AuthorSignatureId</c>, and (first time
    /// only) <c>LockedAtUtc</c>. Optionally sets
    /// <c>EffectiveFromUtc</c> at submission time.
    /// </summary>
    /// <param name="revisionId">Revision to submit. Must be in
    /// <c>Draft</c>.</param>
    /// <param name="reviewerUserIds">Non-empty, deduplicated set of
    /// reviewer user ids. The current user (author) may not appear in
    /// the list.</param>
    /// <param name="effectiveFromUtc">Optional UTC instant from which
    /// the revision becomes Active once approved. May be in the future
    /// per SPEC §3.7. Pass <see langword="null"/> for "active
    /// immediately on approval."</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The persisted revision in its post-submit state.</returns>
    /// <exception cref="System.Collections.Generic.KeyNotFoundException">Thrown
    /// when no revision with the supplied id exists.</exception>
    /// <exception cref="EasySynQ.Services.Authorization.UnauthorizedOperationException">Thrown
    /// when the current user lacks <c>Document.SubmitForReview</c> or
    /// <c>Document.AssignReviewers</c>.</exception>
    /// <exception cref="System.ArgumentException">Thrown when the
    /// reviewer set is empty, contains duplicates, or contains the
    /// author.</exception>
    /// <exception cref="System.InvalidOperationException">Thrown when no
    /// authenticated user is available, or the revision is not in
    /// Draft.</exception>
    Task<DocumentRevision> SubmitForReviewAsync(
        Guid revisionId,
        IReadOnlyCollection<Guid> reviewerUserIds,
        DateTime? effectiveFromUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns an InReview revision to Draft. Discards every
    /// non-Discarded assignment for the revision (per ADR 0008
    /// §"Signatures reset" — both Pending and already-Signed
    /// assignments transition to Discarded; <c>Signature</c> rows are
    /// preserved unchanged in the audit log). Clears
    /// <c>AuthorSignatureId</c> per ADR 0008 C3 plan §G Q3.
    /// <c>LockedAtUtc</c> is preserved (one-way per the SignableEntity
    /// contract).
    /// </summary>
    /// <param name="revisionId">Revision to return to Draft. Must be in
    /// <c>InReview</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The persisted revision in its post-return state.</returns>
    /// <exception cref="System.Collections.Generic.KeyNotFoundException">Thrown
    /// when no revision with the supplied id exists.</exception>
    /// <exception cref="EasySynQ.Services.Authorization.UnauthorizedOperationException">Thrown
    /// when the current user lacks <c>Document.ReturnForEdits</c>.</exception>
    /// <exception cref="System.InvalidOperationException">Thrown when no
    /// authenticated user is available, or the revision is not in
    /// InReview.</exception>
    Task<DocumentRevision> ReturnToDraftAsync(
        Guid revisionId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Records the current user's reviewer signature on the revision,
    /// transitioning their assignment from
    /// <c>Pending</c> to <c>Signed</c>. If this signature completes
    /// the assigned reviewer set, the revision transitions to
    /// <c>Approved</c> in the same transaction; if a prior Active
    /// revision exists for the parent document it transitions to
    /// <c>Superseded</c> in the same transaction; a
    /// <c>DocumentRevisionApprovedEvent</c> is enqueued for dispatch
    /// inside the same <c>SaveChanges</c>.
    /// </summary>
    /// <param name="revisionId">Revision to sign on. Must be in
    /// <c>InReview</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The persisted assignment in its post-sign state.</returns>
    /// <exception cref="System.Collections.Generic.KeyNotFoundException">Thrown
    /// when no revision with the supplied id exists.</exception>
    /// <exception cref="EasySynQ.Services.Authorization.UnauthorizedOperationException">Thrown
    /// when the current user lacks <c>Document.Review</c>.</exception>
    /// <exception cref="System.InvalidOperationException">Thrown when no
    /// authenticated user is available, the revision is not in
    /// InReview, the current user has no Pending assignment on the
    /// revision, or the current user has already signed.</exception>
    Task<DocumentReviewAssignment> SignAsReviewerAsync(
        Guid revisionId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Retires the document. Stages a Retirement
    /// <c>Signature</c>, sets the document's <c>RetiredAtUtc</c>,
    /// <c>RetiredByUserId</c>, <c>RetirementSignatureId</c>, and
    /// archives the current Active revision (transitions its
    /// <c>Lifecycle</c> to <c>Archived</c>). No new revision is
    /// created.
    /// </summary>
    /// <param name="documentId">Document to retire. Must not already be
    /// retired and must have a currently-Active revision per ADR 0008
    /// C3 plan §G Q7.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="System.Collections.Generic.KeyNotFoundException">Thrown
    /// when no document with the supplied id exists.</exception>
    /// <exception cref="EasySynQ.Services.Authorization.UnauthorizedOperationException">Thrown
    /// when the current user lacks <c>Document.Retire</c>.</exception>
    /// <exception cref="System.InvalidOperationException">Thrown when no
    /// authenticated user is available, the document is already
    /// retired, or the document has no currently-Active revision.</exception>
    Task RetireAsync(Guid documentId, CancellationToken cancellationToken);
}
