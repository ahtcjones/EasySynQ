using System.Globalization;

using EasySynQ.Domain;
using EasySynQ.Domain.Entities.Documents;
using EasySynQ.Domain.Enums;
using EasySynQ.Domain.Events.Documents;
using EasySynQ.Services.Abstractions;
using EasySynQ.Services.Authorization;
using EasySynQ.Services.Events;
using EasySynQ.Services.Signatures;
using EasySynQ.Services.Time;
using EasySynQ.Services.Vault;

namespace EasySynQ.Services.Documents;

/// <summary>
/// Production <see cref="IDocumentLifecycleService"/>. Coordinates
/// state-machine transitions across <c>Document</c>,
/// <c>DocumentRevision</c>, <c>DocumentReviewAssignment</c>, and
/// <c>Signature</c> entities; enqueues domain events for transactional
/// dispatch (ADR 0008 C3).
/// </summary>
/// <remarks>
/// Each public method runs in a single <c>SaveChanges</c> transaction
/// (one CorrelationId). Per ADR 0002, the service consumes repositories
/// and the unit of work — never the <c>DbContext</c> directly.
/// </remarks>
public sealed class DocumentLifecycleService : IDocumentLifecycleService
{
    private const string SignedEntityTypeDocument = nameof(Document);
    private const string SignedEntityTypeDocumentRevision = nameof(DocumentRevision);

    private const string InitialRevisionLabel = "Rev A";
    private const string PdfMimeType = "application/pdf";
    private const string PdfFileExtension = ".pdf";

    private readonly IDocumentRepository _documents;
    private readonly IDocumentRevisionRepository _revisions;
    private readonly IDocumentReviewAssignmentRepository _assignments;
    private readonly IDocumentReviewCommentRepository _comments;
    private readonly ISignatureService _signatures;
    private readonly IDomainEventDispatcher _eventDispatcher;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IVaultService _vault;

    /// <summary>Constructs the service over its dependencies.</summary>
    public DocumentLifecycleService(
        IDocumentRepository documents,
        IDocumentRevisionRepository revisions,
        IDocumentReviewAssignmentRepository assignments,
        IDocumentReviewCommentRepository comments,
        ISignatureService signatures,
        IDomainEventDispatcher eventDispatcher,
        ICurrentUserAccessor currentUser,
        IClock clock,
        IUnitOfWork unitOfWork,
        IVaultService vault)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(revisions);
        ArgumentNullException.ThrowIfNull(assignments);
        ArgumentNullException.ThrowIfNull(comments);
        ArgumentNullException.ThrowIfNull(signatures);
        ArgumentNullException.ThrowIfNull(eventDispatcher);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(vault);

        _documents = documents;
        _revisions = revisions;
        _assignments = assignments;
        _comments = comments;
        _signatures = signatures;
        _eventDispatcher = eventDispatcher;
        _currentUser = currentUser;
        _clock = clock;
        _unitOfWork = unitOfWork;
        _vault = vault;
    }

    /// <inheritdoc />
    public async Task<DocumentRevision> SubmitForReviewAsync(
        Guid revisionId,
        IReadOnlyCollection<Guid> reviewerUserIds,
        DateTime? effectiveFromUtc,
        string signingAsRole,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reviewerUserIds);
        ArgumentException.ThrowIfNullOrWhiteSpace(signingAsRole);

        var actorId = RequireAuthenticatedUser();
        RequirePermission(PermissionNames.DocumentSubmitForReview);
        RequirePermission(PermissionNames.DocumentAssignReviewers);

        // Reviewer-set guards per plan §G Q2 / Q5.
        if (reviewerUserIds.Count == 0)
        {
            throw new ArgumentException(
                "Reviewer list must contain at least one reviewer.",
                nameof(reviewerUserIds));
        }
        if (reviewerUserIds.Distinct().Count() != reviewerUserIds.Count)
        {
            throw new ArgumentException(
                "Reviewer list contains duplicate user IDs.",
                nameof(reviewerUserIds));
        }
        if (reviewerUserIds.Contains(actorId))
        {
            throw new ArgumentException(
                "Author cannot review their own document.",
                nameof(reviewerUserIds));
        }

        var revision = await _revisions.GetByIdAsync(revisionId, cancellationToken)
            ?? throw new KeyNotFoundException(
                $"DocumentRevision {revisionId} not found (no row, or row is soft-deleted).");

        if (effectiveFromUtc is { } effective)
        {
            // The entity guard requires Draft state; calling here
            // before Submit() ensures the EffectiveFromUtc edit is
            // permitted. Submit() will then transition to InReview.
            revision.SetEffectiveFromUtc(effective);
        }

        // Stage the author's submission signature first so its Id is
        // available to stamp on the revision via Submit(). Payload
        // format per plan §G Q6 (option a — minimal action+timestamp).
        // Role per ADR 0009 — caller supplies it (UI prompter for
        // multi-role users, literal for single-role).
        var lockedAtUtc = _clock.UtcNow;
        var authorSig = await _signatures.StageSignatureAsync(
            signedEntityType: SignedEntityTypeDocumentRevision,
            signedEntityId: revisionId.ToString(),
            canonicalPayload: BuildSubmitPayload(revisionId, lockedAtUtc),
            signingAsRole: signingAsRole,
            cancellationToken: cancellationToken);

        // Stage one assignment per reviewer. Author may sign as a
        // reviewer in real workflows, but we forbid the author
        // appearing in their OWN reviewer list per Q5.
        foreach (var reviewerId in reviewerUserIds)
        {
            var assignment = new DocumentReviewAssignment(
                id: Guid.NewGuid(),
                documentRevisionId: revisionId,
                reviewerUserId: reviewerId,
                assignedAtUtc: lockedAtUtc,
                assignedByUserId: actorId);
            await _assignments.AddAsync(assignment, cancellationToken);
        }

        revision.Submit(authorSig.Id, lockedAtUtc);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return revision;
    }

    /// <inheritdoc />
    public async Task<DocumentRevision> ReturnToDraftAsync(
        Guid revisionId,
        string reason,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        _ = RequireAuthenticatedUser();
        RequirePermission(PermissionNames.DocumentReturnForEdits);

        var revision = await _revisions.GetByIdAsync(revisionId, cancellationToken)
            ?? throw new KeyNotFoundException(
                $"DocumentRevision {revisionId} not found (no row, or row is soft-deleted).");

        var assignments = await _assignments.GetByRevisionIdAsync(revisionId, cancellationToken);

        // Discard every non-Discarded assignment (both Pending and
        // Signed) per ADR 0008 §"Signatures reset". Signature rows
        // for previously-Signed assignments are NOT deleted — they
        // remain in the audit log.
        foreach (var assignment in assignments)
        {
            if (assignment.Status != DocumentReviewAssignmentStatus.Discarded)
            {
                assignment.Discard();
            }
        }

        // Revision back to Draft + AuthorSignatureId cleared (per Q3)
        // + LastReturnToDraftReason stamped with the supplied reason
        // (ADR 0008 C6b). Audit-row count unchanged at 1+N — the
        // reason rides on the same revision-Update row.
        revision.ReturnToDraft(reason);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return revision;
    }

    /// <inheritdoc />
    public async Task<DocumentReviewAssignment> SignAsReviewerAsync(
        Guid revisionId,
        string signingAsRole,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signingAsRole);

        var actorId = RequireAuthenticatedUser();
        RequirePermission(PermissionNames.DocumentReview);

        var revision = await _revisions.GetByIdAsync(revisionId, cancellationToken)
            ?? throw new KeyNotFoundException(
                $"DocumentRevision {revisionId} not found (no row, or row is soft-deleted).");

        if (revision.Lifecycle != DocumentLifecycle.InReview)
        {
            throw new InvalidOperationException(
                $"Cannot sign as reviewer on revision {revisionId}: current state is " +
                $"'{revision.Lifecycle}', expected '{nameof(DocumentLifecycle.InReview)}'.");
        }

        var assignments = await _assignments.GetByRevisionIdAsync(revisionId, cancellationToken);
        var assignment = assignments.FirstOrDefault(a => a.ReviewerUserId == actorId)
            ?? throw new InvalidOperationException(
                $"User {actorId} is not in the assigned reviewer list for revision {revisionId}.");

        if (assignment.Status == DocumentReviewAssignmentStatus.Signed)
        {
            throw new InvalidOperationException(
                $"User {actorId} has already signed assignment {assignment.Id} on revision {revisionId}.");
        }
        if (assignment.Status == DocumentReviewAssignmentStatus.Discarded)
        {
            throw new InvalidOperationException(
                $"Assignment {assignment.Id} on revision {revisionId} is in Discarded state and cannot be signed.");
        }

        var signedAtUtc = _clock.UtcNow;
        var sig = await _signatures.StageSignatureAsync(
            signedEntityType: SignedEntityTypeDocumentRevision,
            signedEntityId: revisionId.ToString(),
            canonicalPayload: BuildReviewPayload(revisionId, actorId, signedAtUtc),
            signingAsRole: signingAsRole,
            cancellationToken: cancellationToken);

        assignment.RecordSignature(sig.Id, signedAtUtc);

        // If this signature completes the assigned set, transition the
        // revision to Approved and (if applicable) supersede the prior
        // Active revision. The completion check is "every non-discarded
        // assignment is now Signed" — the just-updated row's status is
        // already Signed via RecordSignature above (in-memory mutation
        // visible without a re-query).
        var allSigned = assignments
            .Where(a => a.Status != DocumentReviewAssignmentStatus.Discarded)
            .All(a => a.Status == DocumentReviewAssignmentStatus.Signed);

        if (allSigned)
        {
            // Find prior Active for the parent document. May be null
            // when this is the document's first approved revision.
            var priorActive = await _revisions.GetActiveRevisionAsync(
                revision.DocumentId, signedAtUtc, cancellationToken);

            revision.Approve(signedAtUtc);

            // Supersede-on-approval per plan §G Q9: the prior Active
            // revision flips to Superseded immediately, regardless of
            // whether the new revision's EffectiveFromUtc has been
            // reached. If priorActive is the same row as the one we
            // just approved (defensive — should not happen because the
            // approved one's Lifecycle was InReview when the resolver
            // ran), skip.
            if (priorActive is not null && priorActive.Id != revision.Id)
            {
                priorActive.Supersede();
            }

            _eventDispatcher.Enqueue(new DocumentRevisionApprovedEvent(
                DocumentRevisionId: revision.Id,
                DocumentId: revision.DocumentId,
                PriorRevisionId: priorActive?.Id,
                ApprovedAtUtc: signedAtUtc));
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return assignment;
    }

    /// <inheritdoc />
    public async Task RetireAsync(Guid documentId, string signingAsRole, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signingAsRole);

        var actorId = RequireAuthenticatedUser();
        RequirePermission(PermissionNames.DocumentRetire);

        var document = await _documents.GetByIdAsync(documentId, cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Document {documentId} not found (no row, or row is soft-deleted).");

        // Already-retired check fires BEFORE the Active-revision lookup.
        // Otherwise a second retire would fail with "no Active" because
        // the first retire archived the active revision — masking the
        // real condition (the document is already retired).
        if (document.RetiredAtUtc is not null)
        {
            throw new InvalidOperationException(
                $"Document {documentId} has already been retired at {document.RetiredAtUtc:O}.");
        }

        var retiredAtUtc = _clock.UtcNow;

        var activeRevision = await _revisions.GetActiveRevisionAsync(
            documentId, retiredAtUtc, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Cannot retire document {documentId}: no currently-Active revision.");

        var sig = await _signatures.StageSignatureAsync(
            signedEntityType: SignedEntityTypeDocument,
            signedEntityId: documentId.ToString(),
            canonicalPayload: BuildRetirePayload(documentId, retiredAtUtc),
            signingAsRole: signingAsRole,
            cancellationToken: cancellationToken);

        document.Retire(retiredAtUtc, actorId, sig.Id);
        activeRevision.Archive();

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private Guid RequireAuthenticatedUser()
    {
        if (_currentUser.UserId is null)
        {
            throw new InvalidOperationException(
                "Cannot perform document lifecycle operation: ICurrentUserAccessor.UserId is null. " +
                "Authenticate before calling.");
        }
        return _currentUser.UserId.Value;
    }

    private void RequirePermission(string permissionName)
    {
        if (!_currentUser.Permissions.Contains(permissionName))
        {
            throw UnauthorizedOperationException.ForMissingPermission(permissionName);
        }
    }

    // Canonical payload formats per plan §G Q6 (option a — minimal
    // action+timestamp). C5's PDF viewer integration may layer in a
    // file-content hash later; for C3, the action+timestamp is
    // sufficient to attest "this user performed this action at this
    // moment."
    private static string BuildSubmitPayload(Guid revisionId, DateTime lockedAtUtc) =>
        $"DocumentRevision:{revisionId:D}:Submit:{lockedAtUtc.ToString("O", CultureInfo.InvariantCulture)}";

    private static string BuildReviewPayload(Guid revisionId, Guid reviewerId, DateTime signedAtUtc) =>
        $"DocumentRevision:{revisionId:D}:Review:{reviewerId:D}:{signedAtUtc.ToString("O", CultureInfo.InvariantCulture)}";

    private static string BuildRetirePayload(Guid documentId, DateTime retiredAtUtc) =>
        $"Document:{documentId:D}:Retire:{retiredAtUtc.ToString("O", CultureInfo.InvariantCulture)}";

    /// <inheritdoc />
    public async Task<Document> CreateDocumentAsync(
        string number,
        string title,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(number);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        var actorId = RequireAuthenticatedUser();
        RequirePermission(PermissionNames.DocumentCreate);

        var document = new Document(Guid.NewGuid(), number, title);
        var revision = new DocumentRevision(
            id: Guid.NewGuid(),
            documentId: document.Id,
            revisionLabel: InitialRevisionLabel,
            authorUserId: actorId);

        await _documents.AddAsync(document, cancellationToken);
        await _revisions.AddAsync(revision, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return document;
    }

    /// <inheritdoc />
    public async Task<DocumentRevision> AttachPdfToDraftAsync(
        Guid documentRevisionId,
        Stream pdfContent,
        string originalFileName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pdfContent);
        ArgumentException.ThrowIfNullOrWhiteSpace(originalFileName);

        _ = RequireAuthenticatedUser();
        RequirePermission(PermissionNames.DocumentEditDraft);

        var revision = await _revisions.GetByIdAsync(documentRevisionId, cancellationToken)
            ?? throw new KeyNotFoundException(
                $"DocumentRevision {documentRevisionId} not found (no row, or row is soft-deleted).");

        if (revision.Lifecycle != DocumentLifecycle.Draft)
        {
            throw new InvalidOperationException(
                $"Cannot attach PDF to revision {documentRevisionId}: current state is " +
                $"'{revision.Lifecycle}', expected '{nameof(DocumentLifecycle.Draft)}'.");
        }

        // VaultService.StoreAsync writes the blob row + on-disk file in
        // its own SaveChanges (or dedupes against an existing blob row
        // without writing). Either way, the returned VaultBlob is
        // already persisted; the subsequent revision update writes its
        // own audit row in a second SaveChanges below.
        //
        // The audit-row count for the lifecycle-service operation
        // (revision update only) is therefore:
        //   - fresh content: 2 (VaultBlob Insert from StoreAsync's own
        //     SaveChanges + DocumentRevision Update here).
        //   - dedup hit: 1 (DocumentRevision Update here; StoreAsync
        //     returned an existing row without writing).
        // CorrelationIds across the two SaveChanges differ — they are
        // two transactions, not one. This matches the C6a plan's
        // explicit two-transactions decision ("the create transaction's
        // audit row is meaningful even if the upload fails; the
        // upload's atomicity is its own"), with the same rationale
        // applied to attach: the vault write's atomicity is its own.
        var blob = await _vault.StoreAsync(
            pdfContent,
            PdfMimeType,
            originalFileName,
            PdfFileExtension,
            cancellationToken);

        revision.AttachVaultBlob(blob.Id);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return revision;
    }

    /// <inheritdoc />
    public async Task<Document> EditDraftMetadataAsync(
        Guid documentId,
        string newNumber,
        string newTitle,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(newTitle);

        _ = RequireAuthenticatedUser();
        RequirePermission(PermissionNames.DocumentEditDraft);

        var document = await _documents.GetByIdAsync(documentId, cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Document {documentId} not found (no row, or row is soft-deleted).");

        if (document.RetiredAtUtc is not null)
        {
            throw new InvalidOperationException(
                $"Cannot edit metadata on retired document {documentId}.");
        }

        var latest = await _revisions.GetLatestRevisionAsync(documentId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Cannot edit metadata on document {documentId}: the document has no revisions.");

        if (latest.Lifecycle != DocumentLifecycle.Draft)
        {
            throw new InvalidOperationException(
                $"Cannot edit metadata on document {documentId}: latest revision " +
                $"{latest.Id} is in '{latest.Lifecycle}' state, expected " +
                $"'{nameof(DocumentLifecycle.Draft)}'.");
        }

        document.EditMetadata(newNumber, newTitle);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return document;
    }

    /// <inheritdoc />
    public async Task HardDeleteDraftAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var actorId = RequireAuthenticatedUser();
        RequirePermission(PermissionNames.DocumentHardDelete);

        var document = await _documents.GetByIdAsync(documentId, cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Document {documentId} not found (no row, or row is soft-deleted).");

        var revisions = await _revisions.GetByDocumentIdAsync(documentId, cancellationToken);

        // Defensive multi-revision guard. Documents with multiple
        // revisions have at least one signed revision (only the first
        // revision can be Draft-without-history; subsequent revisions
        // arrive via the not-yet-implemented "new revision" flow which
        // always signs). Hard-deleting any revision past Draft is a
        // SPEC §3.5 violation; refusing on count is the simplest safe
        // guard for C6a's actual scope.
        if (revisions.Count != 1)
        {
            throw new InvalidOperationException(
                $"Cannot hard-delete document {documentId}: expected exactly 1 revision, " +
                $"found {revisions.Count}. Hard-delete is only supported for " +
                $"single-revision Drafts in C6a scope.");
        }

        var revision = revisions[0];

        if (revision.Lifecycle != DocumentLifecycle.Draft)
        {
            throw new InvalidOperationException(
                $"Cannot hard-delete document {documentId}: revision " +
                $"{revision.Id} is in '{revision.Lifecycle}' state, expected " +
                $"'{nameof(DocumentLifecycle.Draft)}'.");
        }

        if (revision.AuthorUserId != actorId)
        {
            throw new InvalidOperationException(
                $"Cannot hard-delete document {documentId}: current user {actorId} is not " +
                $"the author of revision {revision.Id} (author is {revision.AuthorUserId}).");
        }

        // Two HardDelete-action audit rows are written by the audit
        // interceptor per ADR 0002 — one for the Document, one for the
        // DocumentRevision — both sharing one CorrelationId via the
        // per-save fallback. The pre-delete JSON snapshot of each
        // entity is captured in the audit row's "before" field.
        _revisions.HardDelete(revision);
        _documents.HardDelete(document);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<DocumentReviewComment> AddCommentAsync(
        Guid revisionId,
        string bodyText,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyText);

        var actorId = RequireAuthenticatedUser();
        RequirePermission(PermissionNames.DocumentReview);

        var revision = await _revisions.GetByIdAsync(revisionId, cancellationToken)
            ?? throw new KeyNotFoundException(
                $"DocumentRevision {revisionId} not found (no row, or row is soft-deleted).");

        if (revision.Lifecycle != DocumentLifecycle.InReview)
        {
            throw new InvalidOperationException(
                $"Cannot add a comment to revision {revisionId}: current state is " +
                $"'{revision.Lifecycle}', expected '{nameof(DocumentLifecycle.InReview)}'.");
        }

        var comment = new DocumentReviewComment(
            id: Guid.NewGuid(),
            documentRevisionId: revisionId,
            authorUserId: actorId,
            bodyText: bodyText,
            createdAtUtc: _clock.UtcNow);

        await _comments.AddAsync(comment, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return comment;
    }
}
