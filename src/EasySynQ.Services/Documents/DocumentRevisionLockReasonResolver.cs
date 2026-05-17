using System.Globalization;

using EasySynQ.Domain;
using EasySynQ.Domain.Entities.Audit;
using EasySynQ.Domain.Entities.Documents;
using EasySynQ.Domain.Enums;
using EasySynQ.Services.Abstractions;
using EasySynQ.Services.LockReasons;

namespace EasySynQ.Services.Documents;

/// <summary>
/// Resolves <see cref="LockReason"/> chains for
/// <see cref="LockedEntityTypes.DocumentRevision"/> lockouts
/// (ADR 0012 C7a). Phase 2 revision-level lockouts:
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>L1 Approved</b> — signed off; all reviewer signatures
///   captured; not editable.</item>
///   <item><b>L2 InReview</b> — submitted for review; pending
///   reviewer signatures; author lock.</item>
///   <item><b>L3 Superseded</b> — a successor revision has been
///   approved and now represents the document.</item>
///   <item><b>L4 Archived</b> — parent <see cref="Document"/> has
///   been retired.</item>
///   <item><b>L6 Soft-deleted</b> — administratively removed.
///   Takes precedence over L1–L4 (a soft-deleted superseded revision
///   surfaces the soft-delete cause, not the lifecycle state).</item>
/// </list>
/// <para>
/// Returns <see langword="null"/> for <see cref="DocumentLifecycle.Draft"/>
/// (not a lockout — the author may freely edit) and for unknown ids.
/// </para>
/// </remarks>
public sealed class DocumentRevisionLockReasonResolver : ILockReasonResolver
{
    private readonly IDocumentRevisionRepository _revisions;

    /// <inheritdoc />
    public string LockedEntityType => LockedEntityTypes.DocumentRevision;

    /// <summary>Constructs the resolver over the supplied revision
    /// repository.</summary>
    /// <param name="revisions">Repository surface for
    /// <see cref="DocumentRevision"/> rows.</param>
    public DocumentRevisionLockReasonResolver(IDocumentRevisionRepository revisions)
    {
        ArgumentNullException.ThrowIfNull(revisions);
        _revisions = revisions;
    }

    /// <inheritdoc />
    public async Task<LockReason?> ResolveAsync(
        string lockedEntityId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockedEntityId);

        if (!Guid.TryParse(lockedEntityId, out var revisionId))
        {
            return null;
        }

        var revision = await _revisions
            .GetByIdIncludingDeletedAsync(revisionId, cancellationToken);
        if (revision is null)
        {
            return null;
        }

        // L6 takes precedence: a soft-deleted row's primary cause is
        // the soft-delete, not whatever lifecycle state it held when
        // removed.
        if (revision.IsDeleted)
        {
            return DocumentLockReasonResolver.BuildSoftDeleteChain(
                LockedEntityType,
                revision.Id,
                revision.RevisionLabel,
                revision.ModifiedUtc,
                revision.ModifiedBy);
        }

        switch (revision.Lifecycle)
        {
            case DocumentLifecycle.Draft:
                // Not locked — author may edit freely.
                return null;

            case DocumentLifecycle.InReview:
                return BuildInReviewChain(revision);

            case DocumentLifecycle.Approved:
                return BuildApprovedChain(revision);

            case DocumentLifecycle.Superseded:
                return await BuildSupersededChainAsync(revision, cancellationToken);

            case DocumentLifecycle.Archived:
                return BuildArchivedChain(revision);

            default:
                // Defensive — every DocumentLifecycle value above is
                // accounted for. A new lifecycle state added without a
                // matching resolver branch surfaces here as "not
                // locked", which is wrong but recoverable; the
                // resolver-completeness integration test (planned for
                // C7b) catches the gap.
                return null;
        }
    }

    private static LockReason BuildInReviewChain(DocumentRevision revision)
    {
        // LockedAtUtc is guaranteed non-null in InReview/Approved/
        // Superseded/Archived states (SignableEntity contract — set
        // on submission, never cleared). The defensive bang is fine
        // here.
        var lockedAt = revision.LockedAtUtc!.Value;

        var link = new LockReasonLink(
            tag: LockedEntityTypes.DocumentRevision,
            id: revision.RevisionLabel,
            detail: string.Create(
                CultureInfo.InvariantCulture,
                $"In Review since {lockedAt:yyyy-MM-dd HH:mm:ss} UTC. Waiting on reviewer signatures; author may not edit until the revision returns to Draft."),
            navigationEntityType: null,
            navigationEntityId: null,
            because: null,
            isTerminal: true);

        return new LockReason(
            id: Guid.NewGuid(),
            lockedEntityType: LockedEntityTypes.DocumentRevision,
            lockedEntityId: revision.Id.ToString("D", CultureInfo.InvariantCulture),
            chain: [link]);
    }

    private static LockReason BuildApprovedChain(DocumentRevision revision)
    {
        var approvedAt = revision.ApprovedAtUtc!.Value;

        var link = new LockReasonLink(
            tag: LockedEntityTypes.DocumentRevision,
            id: revision.RevisionLabel,
            detail: string.Create(
                CultureInfo.InvariantCulture,
                $"Approved on {approvedAt:yyyy-MM-dd HH:mm:ss} UTC. All reviewer signatures captured; the revision is signed off and not editable."),
            navigationEntityType: null,
            navigationEntityId: null,
            because: null,
            isTerminal: true);

        return new LockReason(
            id: Guid.NewGuid(),
            lockedEntityType: LockedEntityTypes.DocumentRevision,
            lockedEntityId: revision.Id.ToString("D", CultureInfo.InvariantCulture),
            chain: [link]);
    }

    private async Task<LockReason> BuildSupersededChainAsync(
        DocumentRevision revision,
        CancellationToken cancellationToken)
    {
        // Find the successor revision — the most-recently-approved
        // sibling revision of the same Document with an
        // ApprovedAtUtc strictly later than this one's. There may be
        // multiple superseding revisions in a long history; the link
        // shows the immediate successor (next-by-ApprovedAtUtc),
        // which is the chain link the inspector renders. Subsequent
        // supersedings are reachable by following the chain from the
        // successor.
        var siblings = await _revisions.GetByDocumentIdAsync(
            revision.DocumentId, cancellationToken);

        var successor = siblings
            .Where(r => r.Id != revision.Id
                     && r.ApprovedAtUtc.HasValue
                     && r.ApprovedAtUtc.Value > (revision.ApprovedAtUtc ?? DateTime.MinValue))
            .OrderBy(r => r.ApprovedAtUtc!.Value)
            .FirstOrDefault();

        var thisLink = new LockReasonLink(
            tag: LockedEntityTypes.DocumentRevision,
            id: revision.RevisionLabel,
            detail: "Superseded by a later approved revision.",
            navigationEntityType: successor is null ? null : LockedEntityTypes.DocumentRevision,
            navigationEntityId: successor?.Id.ToString("D", CultureInfo.InvariantCulture),
            because: "superseded by",
            isTerminal: false);

        // Successor row missing is unexpected (Supersede() runs in the
        // same transaction as the successor's Approve) but the resolver
        // remains resilient — emit a terminal placeholder so the chain
        // remains valid per LockReason's constructor invariants.
        LockReasonLink terminal;
        if (successor is null)
        {
            terminal = new LockReasonLink(
                tag: LockedEntityTypes.DocumentRevision,
                id: "(successor not found)",
                detail: "The successor revision could not be located. This is unexpected — the supersede transition records the successor atomically with the approval.",
                navigationEntityType: null,
                navigationEntityId: null,
                because: null,
                isTerminal: true);
        }
        else
        {
            var successorApprovedAt = successor.ApprovedAtUtc!.Value;
            terminal = new LockReasonLink(
                tag: LockedEntityTypes.DocumentRevision,
                id: successor.RevisionLabel,
                detail: string.Create(
                    CultureInfo.InvariantCulture,
                    $"Approved on {successorApprovedAt:yyyy-MM-dd HH:mm:ss} UTC; now the latest approved revision of this document."),
                navigationEntityType: null,
                navigationEntityId: null,
                because: null,
                isTerminal: true);
        }

        return new LockReason(
            id: Guid.NewGuid(),
            lockedEntityType: LockedEntityTypes.DocumentRevision,
            lockedEntityId: revision.Id.ToString("D", CultureInfo.InvariantCulture),
            chain: [thisLink, terminal]);
    }

    private static LockReason BuildArchivedChain(DocumentRevision revision)
    {
        // The parent Document is the root cause — Archive() runs only
        // from the Document.Retire path. The chain navigates the user
        // toward the document detail view where the retirement
        // signature is visible.
        var documentId = revision.DocumentId;

        var thisLink = new LockReasonLink(
            tag: LockedEntityTypes.DocumentRevision,
            id: revision.RevisionLabel,
            detail: "Archived. The parent Document was retired; this revision is preserved for audit but is no longer in use.",
            navigationEntityType: LockedEntityTypes.Document,
            navigationEntityId: documentId.ToString("D", CultureInfo.InvariantCulture),
            because: "parent Document was retired",
            isTerminal: false);

        var terminal = new LockReasonLink(
            tag: LockedEntityTypes.Document,
            id: documentId.ToString("D", CultureInfo.InvariantCulture),
            detail: "Retired Document. See the parent for the retirement signature and timestamp.",
            navigationEntityType: null,
            navigationEntityId: null,
            because: null,
            isTerminal: true);

        return new LockReason(
            id: Guid.NewGuid(),
            lockedEntityType: LockedEntityTypes.DocumentRevision,
            lockedEntityId: revision.Id.ToString("D", CultureInfo.InvariantCulture),
            chain: [thisLink, terminal]);
    }
}
