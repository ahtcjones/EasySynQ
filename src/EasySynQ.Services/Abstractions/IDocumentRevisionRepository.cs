using EasySynQ.Domain.Entities.Documents;

namespace EasySynQ.Services.Abstractions;

/// <summary>
/// Repository surface for <see cref="DocumentRevision"/> rows (ADR 0008
/// C3). Adds the as-of resolver (<see cref="GetActiveRevisionAsync"/>)
/// that derives the "Active" sub-state per ADR 0008 §"Effective dating
/// on DocumentRevision", and a per-document fetch used by the lifecycle
/// service to find any revision in any state for a given parent.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the as-of resolver lives here, not as an EF Core global query
/// filter.</b> Effective dating on <c>DocumentRevision</c> is a service-
/// time concern, not a query-filter concern: the same revision row may
/// be Active at one instant and not at another, but we do not want
/// general queries against <c>DbSet&lt;DocumentRevision&gt;</c> to
/// silently exclude rows. The as-of resolution is opt-in via this
/// repository method, mirroring ADR 0007's
/// <c>IUserRoleRepository.GetEffectiveRoleNamesAsync</c> precedent.
/// </para>
/// <para>
/// <b>Distinction from <c>IEffectiveDated</c>.</b>
/// <c>RolePermission</c> / <c>UserPermission</c> implement
/// <c>IEffectiveDated</c> and pick up the global as-of filter
/// configured per ADR 0005. <c>DocumentRevision</c> deliberately does
/// not implement that interface; the "Active" determination requires
/// reasoning across multiple revisions of the same document
/// (latest-by-ApprovedAtUtc tie-break) which the global filter cannot
/// express.
/// </para>
/// </remarks>
public interface IDocumentRevisionRepository : IRepository<DocumentRevision, Guid>
{
    /// <summary>
    /// Returns the revision whose Approved-state covers
    /// <paramref name="asOfUtc"/> for the given document, or
    /// <see langword="null"/> if no revision is currently Active. A
    /// revision is Active when:
    /// <list type="bullet">
    ///   <item><c>Lifecycle == Approved</c> — Superseded and Archived
    ///   are excluded.</item>
    ///   <item><c>EffectiveFromUtc</c> is null OR
    ///   <c>EffectiveFromUtc &lt;= asOfUtc</c>.</item>
    /// </list>
    /// When multiple revisions match, the one with the latest
    /// <c>ApprovedAtUtc</c> wins.
    /// </summary>
    /// <remarks>
    /// Per ADR 0008 C3 plan §G Q9: when a successor revision is
    /// approved, the prior Active revision is immediately
    /// <see cref="EasySynQ.Domain.Enums.DocumentLifecycle.Superseded"/>
    /// regardless of the successor's <c>EffectiveFromUtc</c>. This
    /// method therefore returns <see langword="null"/> during the gap
    /// between a successor's approval and its effective date — both
    /// the prior (Superseded) and the successor (Approved-but-not-yet-
    /// effective) are excluded. The class remarks on the lifecycle
    /// service document this UX-visible behavior.
    /// </remarks>
    /// <param name="documentId">Parent document id.</param>
    /// <param name="asOfUtc">UTC instant at which to evaluate the
    /// active revision.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The revision currently Active at
    /// <paramref name="asOfUtc"/>, or <see langword="null"/>.</returns>
    Task<DocumentRevision?> GetActiveRevisionAsync(
        Guid documentId,
        DateTime asOfUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns the most recently-created revision of
    /// <paramref name="documentId"/> regardless of lifecycle state, or
    /// <see langword="null"/> if the document has no revisions. Backs
    /// the C6a Document detail view, which displays whatever revision
    /// currently represents the document (Draft for newly-authored
    /// documents; Approved-but-not-yet-effective during the gap window;
    /// Active otherwise). Ordering is by <c>CreatedUtc</c> descending.
    /// </summary>
    /// <param name="documentId">Parent document id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The latest revision, or <see langword="null"/>.</returns>
    Task<DocumentRevision?> GetLatestRevisionAsync(
        Guid documentId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns every revision of <paramref name="documentId"/>
    /// regardless of lifecycle state, ordered by <c>CreatedUtc</c>
    /// ascending. Used by the C6a hard-delete-draft path as a
    /// defensive single-revision check (a Document with multiple
    /// revisions has at least one signed revision and is therefore
    /// outside the SPEC §3.5 hard-delete boundary).
    /// </summary>
    /// <param name="documentId">Parent document id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>All revisions for the document.</returns>
    Task<IReadOnlyList<DocumentRevision>> GetByDocumentIdAsync(
        Guid documentId,
        CancellationToken cancellationToken);
}
