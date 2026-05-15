using EasySynQ.Data.Context;
using EasySynQ.Domain.Entities.Documents;
using EasySynQ.Services.Abstractions;

using Microsoft.EntityFrameworkCore;

namespace EasySynQ.Data.Repositories;

/// <summary>
/// EF Core implementation of
/// <see cref="IDocumentReviewAssignmentRepository"/>. Adds the per-
/// revision fetch used by the lifecycle service to determine reviewer-
/// completion state (ADR 0008 C3).
/// </summary>
public sealed class DocumentReviewAssignmentRepository
    : Repository<DocumentReviewAssignment, Guid>, IDocumentReviewAssignmentRepository
{
    /// <summary>Constructs the repository over the supplied DbContext.</summary>
    public DocumentReviewAssignmentRepository(EasySynQDbContext context)
        : base(context)
    {
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DocumentReviewAssignment>> GetByRevisionIdAsync(
        Guid documentRevisionId,
        CancellationToken cancellationToken)
    {
        if (documentRevisionId == Guid.Empty)
        {
            throw new ArgumentException(
                "DocumentRevisionId must not be Guid.Empty.", nameof(documentRevisionId));
        }

        return await Query()
            .Where(a => a.DocumentRevisionId == documentRevisionId)
            .ToListAsync(cancellationToken);
    }
}
