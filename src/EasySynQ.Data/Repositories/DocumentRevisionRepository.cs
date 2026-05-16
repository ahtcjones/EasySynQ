using EasySynQ.Data.Context;
using EasySynQ.Domain.Entities.Documents;
using EasySynQ.Domain.Enums;
using EasySynQ.Services.Abstractions;

using Microsoft.EntityFrameworkCore;

namespace EasySynQ.Data.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IDocumentRevisionRepository"/>.
/// Adds the as-of resolver (<see cref="GetActiveRevisionAsync"/>) that
/// derives the "Active" sub-state per ADR 0008 §"Effective dating on
/// DocumentRevision".
/// </summary>
/// <remarks>
/// The query relies on the standard soft-delete filter being active
/// (Superseded / Archived / soft-deleted rows are excluded). Per plan
/// §G Q9, supersede-on-approval semantics mean that during the gap
/// between a successor's approval and its EffectiveFromUtc, this
/// resolver returns <see langword="null"/> — both the prior
/// (Superseded) and the successor (Approved-but-not-yet-effective) are
/// excluded. The lifecycle service's class remarks document this
/// UX-visible behavior.
/// </remarks>
public sealed class DocumentRevisionRepository
    : Repository<DocumentRevision, Guid>, IDocumentRevisionRepository
{
    /// <summary>Constructs the repository over the supplied DbContext.</summary>
    public DocumentRevisionRepository(EasySynQDbContext context)
        : base(context)
    {
    }

    /// <inheritdoc />
    public Task<DocumentRevision?> GetActiveRevisionAsync(
        Guid documentId,
        DateTime asOfUtc,
        CancellationToken cancellationToken)
    {
        if (documentId == Guid.Empty)
        {
            throw new ArgumentException(
                "DocumentId must not be Guid.Empty.", nameof(documentId));
        }
        if (asOfUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "AsOfUtc must have DateTimeKind.Utc.", nameof(asOfUtc));
        }

        return Query()
            .Where(r => r.DocumentId == documentId
                     && r.Lifecycle == DocumentLifecycle.Approved
                     && (r.EffectiveFromUtc == null || r.EffectiveFromUtc <= asOfUtc))
            .OrderByDescending(r => r.ApprovedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<DocumentRevision?> GetLatestRevisionAsync(
        Guid documentId,
        CancellationToken cancellationToken)
    {
        if (documentId == Guid.Empty)
        {
            throw new ArgumentException(
                "DocumentId must not be Guid.Empty.", nameof(documentId));
        }

        return Query()
            .Where(r => r.DocumentId == documentId)
            .OrderByDescending(r => r.CreatedUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DocumentRevision>> GetByDocumentIdAsync(
        Guid documentId,
        CancellationToken cancellationToken)
    {
        if (documentId == Guid.Empty)
        {
            throw new ArgumentException(
                "DocumentId must not be Guid.Empty.", nameof(documentId));
        }

        return await Query()
            .Where(r => r.DocumentId == documentId)
            .OrderBy(r => r.CreatedUtc)
            .ToListAsync(cancellationToken);
    }
}
