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
    /// <param name="signingAsRole">Role the author is signing the
    /// submission as (ADR 0009). Must be a member of the current
    /// user's effective roles. The UI signing-flow prompter resolves
    /// this for multi-role users; single-role users pass their only
    /// role literally.</param>
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
    /// authenticated user is available, the revision is not in Draft,
    /// or <paramref name="signingAsRole"/> is not a role the user
    /// holds.</exception>
    Task<DocumentRevision> SubmitForReviewAsync(
        Guid revisionId,
        IReadOnlyCollection<Guid> reviewerUserIds,
        DateTime? effectiveFromUtc,
        string signingAsRole,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns an InReview revision to Draft. Discards every
    /// non-Discarded assignment for the revision (per ADR 0008
    /// §"Signatures reset" — both Pending and already-Signed
    /// assignments transition to Discarded; <c>Signature</c> rows are
    /// preserved unchanged in the audit log). Clears
    /// <c>AuthorSignatureId</c> per ADR 0008 C3 plan §G Q3 and
    /// stamps <see cref="DocumentRevision.LastReturnToDraftReason"/>
    /// with <paramref name="reason"/> so the author sees why the
    /// revision came back. <c>LockedAtUtc</c> is preserved (one-way
    /// per the SignableEntity contract).
    /// </summary>
    /// <remarks>
    /// <b>Audit-row count: 1 + N.</b> Revision Update +
    /// N assignment Updates (N = number of non-Discarded
    /// assignments). The reason travels on the revision-Update row's
    /// <c>After</c> snapshot; adding the reason parameter does not
    /// change the audit-row formula (ADR 0008 C6b).
    /// </remarks>
    /// <param name="revisionId">Revision to return to Draft. Must be in
    /// <c>InReview</c>.</param>
    /// <param name="reason">Free-form reason supplied by the caller.
    /// Must not be <see langword="null"/>, empty, or whitespace per
    /// the C6b plan §E "required-reason text box" decision.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The persisted revision in its post-return state.</returns>
    /// <exception cref="System.Collections.Generic.KeyNotFoundException">Thrown
    /// when no revision with the supplied id exists.</exception>
    /// <exception cref="EasySynQ.Services.Authorization.UnauthorizedOperationException">Thrown
    /// when the current user lacks <c>Document.ReturnForEdits</c>.</exception>
    /// <exception cref="System.InvalidOperationException">Thrown when no
    /// authenticated user is available, or the revision is not in
    /// InReview.</exception>
    /// <exception cref="System.ArgumentException">Thrown when
    /// <paramref name="reason"/> is null, empty, or whitespace.</exception>
    Task<DocumentRevision> ReturnToDraftAsync(
        Guid revisionId,
        string reason,
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
    /// <param name="signingAsRole">Role the reviewer is signing as
    /// (ADR 0009). Must be a member of the current user's effective
    /// roles.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The persisted assignment in its post-sign state.</returns>
    /// <exception cref="System.Collections.Generic.KeyNotFoundException">Thrown
    /// when no revision with the supplied id exists.</exception>
    /// <exception cref="EasySynQ.Services.Authorization.UnauthorizedOperationException">Thrown
    /// when the current user lacks <c>Document.Review</c>.</exception>
    /// <exception cref="System.InvalidOperationException">Thrown when no
    /// authenticated user is available, the revision is not in
    /// InReview, the current user has no Pending assignment on the
    /// revision, the current user has already signed, or
    /// <paramref name="signingAsRole"/> is not a role the user
    /// holds.</exception>
    Task<DocumentReviewAssignment> SignAsReviewerAsync(
        Guid revisionId,
        string signingAsRole,
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
    /// <param name="signingAsRole">Role the user is signing the
    /// retirement as (ADR 0009). Must be a member of the current
    /// user's effective roles.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="System.Collections.Generic.KeyNotFoundException">Thrown
    /// when no document with the supplied id exists.</exception>
    /// <exception cref="EasySynQ.Services.Authorization.UnauthorizedOperationException">Thrown
    /// when the current user lacks <c>Document.Retire</c>.</exception>
    /// <exception cref="System.InvalidOperationException">Thrown when no
    /// authenticated user is available, the document is already
    /// retired, the document has no currently-Active revision, or
    /// <paramref name="signingAsRole"/> is not a role the user
    /// holds.</exception>
    Task RetireAsync(Guid documentId, string signingAsRole, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a new internal <see cref="Document"/> with its first
    /// <see cref="DocumentRevision"/> in <c>Draft</c> state (ADR 0008
    /// C6a). Atomic in one <c>SaveChanges</c>: writes the
    /// <c>Document</c> row and the initial Draft revision (with
    /// hardcoded label <c>"Rev A"</c>, no <c>VaultBlobId</c>,
    /// <c>AuthorUserId</c> set to the current user) in a single
    /// transaction.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Authoring belongs to the lifecycle service.</b> Document
    /// creation is a Draft-state operation; the service that owns
    /// Draft-state lifecycle transitions also owns Draft creation. No
    /// separate authoring service exists per the C6a plan.
    /// </para>
    /// <para>
    /// <b>"Rev A" is a deliberate C6a default.</b> The revision label is
    /// a free string per ADR 0008; user-selectable initial-label
    /// affordance is polish that lands in a later commit.
    /// </para>
    /// <para>
    /// <b>Audit-row count: 2.</b> Document Insert + DocumentRevision
    /// Insert. Both rows share one CorrelationId via the per-save
    /// fallback.
    /// </para>
    /// </remarks>
    /// <param name="number">Org-assigned document number. Must not be
    /// <see langword="null"/>, empty, or whitespace.</param>
    /// <param name="title">Human-readable document title. Must not be
    /// <see langword="null"/>, empty, or whitespace.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The persisted document.</returns>
    /// <exception cref="EasySynQ.Services.Authorization.UnauthorizedOperationException">Thrown
    /// when the current user lacks <c>Document.Create</c>.</exception>
    /// <exception cref="System.ArgumentException">Thrown when
    /// <paramref name="number"/> or <paramref name="title"/> fails
    /// validation.</exception>
    /// <exception cref="System.InvalidOperationException">Thrown when
    /// no authenticated user is available.</exception>
    Task<Document> CreateDocumentAsync(
        string number,
        string title,
        CancellationToken cancellationToken);

    /// <summary>
    /// Attaches (or replaces) the PDF content of a Draft revision
    /// (ADR 0008 C6a). Calls
    /// <see cref="EasySynQ.Services.Vault.IVaultService.StoreAsync"/>
    /// to write content-addressed storage (dedupes on content hash),
    /// then sets the revision's <c>VaultBlobId</c> to the returned
    /// blob's id. Atomic in one <c>SaveChanges</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Replace-PDF reuses this method.</b> Calling on a revision
    /// that already has a <c>VaultBlobId</c> overwrites the reference;
    /// the orphaned blob remains in the vault per the C6a
    /// "deferred indefinitely" cleanup decision (content-addressed
    /// dedup means orphans don't bloat materially).
    /// </para>
    /// <para>
    /// <b>Audit-row count.</b> Two on fresh content (VaultBlob Insert
    /// from VaultService.StoreAsync's own SaveChanges + DocumentRevision
    /// Update from this service's SaveChanges). One on dedup hit
    /// (DocumentRevision Update only — VaultService returns the
    /// existing blob row without inserting). The two SaveChanges
    /// calls are independent transactions; in production each gets
    /// its own per-save-fallback CorrelationId. The split is
    /// deliberate per the C6a plan ("the upload's atomicity is its
    /// own").
    /// </para>
    /// </remarks>
    /// <param name="documentRevisionId">Revision to attach to. Must
    /// be in <c>Draft</c>.</param>
    /// <param name="pdfContent">PDF content stream. Read forward to
    /// end by the vault; caller retains ownership.</param>
    /// <param name="originalFileName">Display name for provenance.
    /// Stored on the <c>VaultBlob</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated revision in its post-attach state.</returns>
    /// <exception cref="EasySynQ.Services.Authorization.UnauthorizedOperationException">Thrown
    /// when the current user lacks <c>Document.EditDraft</c>.</exception>
    /// <exception cref="System.Collections.Generic.KeyNotFoundException">Thrown
    /// when no revision with the supplied id exists.</exception>
    /// <exception cref="System.InvalidOperationException">Thrown when
    /// no authenticated user is available, or the revision is not in
    /// <c>Draft</c>.</exception>
    /// <exception cref="System.ArgumentException">Thrown when
    /// <paramref name="originalFileName"/> fails validation.</exception>
    /// <exception cref="System.ArgumentNullException">Thrown when
    /// <paramref name="pdfContent"/> is <see langword="null"/>.</exception>
    Task<DocumentRevision> AttachPdfToDraftAsync(
        Guid documentRevisionId,
        Stream pdfContent,
        string originalFileName,
        CancellationToken cancellationToken);

    /// <summary>
    /// Updates the <see cref="Document.Number"/> and
    /// <see cref="Document.Title"/> of a Document whose latest revision
    /// is in <c>Draft</c> (ADR 0008 C6a). Atomic in one
    /// <c>SaveChanges</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why the latest-revision-Draft check.</b> Metadata edits per
    /// SPEC §3.5 are permitted only on records that have not crossed
    /// the immutability boundary. <see cref="Document"/> itself carries
    /// no signature; the boundary is on its
    /// <see cref="DocumentRevision"/>s. A document whose latest revision
    /// is past Draft has already been signed (the author's submission
    /// signature) and is therefore immutable-soft-delete-only. The
    /// service loads the latest revision to verify the boundary
    /// before permitting the edit.
    /// </para>
    /// <para>
    /// <b>Audit-row count: 1.</b> Document Update. CorrelationId per
    /// the per-save fallback.
    /// </para>
    /// </remarks>
    /// <param name="documentId">Document to edit.</param>
    /// <param name="newNumber">Updated document number. Must not be
    /// <see langword="null"/>, empty, or whitespace.</param>
    /// <param name="newTitle">Updated document title. Must not be
    /// <see langword="null"/>, empty, or whitespace.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated document.</returns>
    /// <exception cref="EasySynQ.Services.Authorization.UnauthorizedOperationException">Thrown
    /// when the current user lacks <c>Document.EditDraft</c>.</exception>
    /// <exception cref="System.Collections.Generic.KeyNotFoundException">Thrown
    /// when no document with the supplied id exists.</exception>
    /// <exception cref="System.InvalidOperationException">Thrown when
    /// no authenticated user is available, the document is retired,
    /// the document has no revisions, or the document's latest
    /// revision is not in <c>Draft</c>.</exception>
    /// <exception cref="System.ArgumentException">Thrown when
    /// <paramref name="newNumber"/> or <paramref name="newTitle"/>
    /// fails validation.</exception>
    Task<Document> EditDraftMetadataAsync(
        Guid documentId,
        string newNumber,
        string newTitle,
        CancellationToken cancellationToken);

    /// <summary>
    /// Hard-deletes a single-revision Draft <see cref="Document"/> and
    /// its sole <see cref="DocumentRevision"/> in one transaction
    /// (ADR 0008 C6a, SPEC §3.5, ADR 0002). The operational rows are
    /// removed; the matching pair of <c>HardDelete</c>-action audit
    /// rows is written and preserved in the append-only audit log.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Restricted to single-revision Drafts authored by the
    /// caller.</b> The service requires:
    /// </para>
    /// <list type="bullet">
    /// <item>the document has exactly one revision (multi-revision
    /// documents have at least one signed revision and are outside
    /// the hard-delete boundary);</item>
    /// <item>that revision is in <c>Draft</c>;</item>
    /// <item>that revision's <c>AuthorUserId</c> matches the current
    /// user (the author-only rule from the C6a brief).</item>
    /// </list>
    /// <para>
    /// <b>Audit-row count: 2.</b> Document HardDelete + DocumentRevision
    /// HardDelete. Both rows share one CorrelationId via the per-save
    /// fallback. Per ADR 0002, each carries the full pre-delete JSON
    /// snapshot in the <c>before</c> field.
    /// </para>
    /// </remarks>
    /// <param name="documentId">Document to hard-delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="EasySynQ.Services.Authorization.UnauthorizedOperationException">Thrown
    /// when the current user lacks <c>Document.HardDelete</c>.</exception>
    /// <exception cref="System.Collections.Generic.KeyNotFoundException">Thrown
    /// when no document with the supplied id exists.</exception>
    /// <exception cref="System.InvalidOperationException">Thrown when
    /// no authenticated user is available, the document has zero or
    /// multiple revisions, the single revision is not in <c>Draft</c>,
    /// or the current user is not the revision's author.</exception>
    Task HardDeleteDraftAsync(Guid documentId, CancellationToken cancellationToken);

    /// <summary>
    /// Adds a reviewer comment to a revision currently in
    /// <see cref="EasySynQ.Domain.Enums.DocumentLifecycle.InReview"/>
    /// (ADR 0008 C6b). Captures the author's <c>UserId</c> from the
    /// current-user accessor and the comment's
    /// <c>CreatedAtUtc</c> from the clock; the body text is
    /// caller-supplied.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Audit-row count: 1.</b> <see cref="DocumentReviewComment"/>
    /// Insert. One <c>SaveChanges</c>.
    /// </para>
    /// <para>
    /// <b>Permission gate.</b> The same <c>Document.Review</c>
    /// permission that authorizes reviewer signatures also
    /// authorizes commenting. The author themself, if they also
    /// hold <c>Document.Review</c> (e.g., author-as-own-reviewer in
    /// permissive deployments), may comment via this method.
    /// </para>
    /// <para>
    /// <b>State gate.</b> Comments are accepted only while the
    /// revision is in InReview. Once the revision returns to Draft
    /// or transitions to Approved, the comment surface closes;
    /// comment edits and deletions are deferred per the C6b plan's
    /// out-of-scope list.
    /// </para>
    /// </remarks>
    /// <param name="revisionId">Revision to comment on. Must be in
    /// <c>InReview</c>.</param>
    /// <param name="bodyText">Comment body. Must not be
    /// <see langword="null"/>, empty, or whitespace.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The persisted comment in its post-insert state.</returns>
    /// <exception cref="System.Collections.Generic.KeyNotFoundException">Thrown
    /// when no revision with the supplied id exists.</exception>
    /// <exception cref="EasySynQ.Services.Authorization.UnauthorizedOperationException">Thrown
    /// when the current user lacks <c>Document.Review</c>.</exception>
    /// <exception cref="System.InvalidOperationException">Thrown when
    /// no authenticated user is available, or the revision is not in
    /// InReview.</exception>
    /// <exception cref="System.ArgumentException">Thrown when
    /// <paramref name="bodyText"/> is null, empty, or whitespace.</exception>
    Task<DocumentReviewComment> AddCommentAsync(
        Guid revisionId,
        string bodyText,
        CancellationToken cancellationToken);
}
