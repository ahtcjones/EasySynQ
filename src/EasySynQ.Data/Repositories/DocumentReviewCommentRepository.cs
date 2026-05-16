using EasySynQ.Data.Context;
using EasySynQ.Domain.Entities.Documents;
using EasySynQ.Services.Abstractions;

using Microsoft.EntityFrameworkCore;

namespace EasySynQ.Data.Repositories;

/// <summary>
/// EF Core implementation of
/// <see cref="IDocumentReviewCommentRepository"/>. Adds the per-
/// revision fetch used by the C6b detail-view comment panel.
/// </summary>
public sealed class DocumentReviewCommentRepository
    : Repository<DocumentReviewComment, Guid>, IDocumentReviewCommentRepository
{
    /// <summary>Constructs the repository over the supplied DbContext.</summary>
    public DocumentReviewCommentRepository(EasySynQDbContext context)
        : base(context)
    {
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DocumentReviewComment>> GetByRevisionIdAsync(
        Guid documentRevisionId,
        CancellationToken cancellationToken)
    {
        if (documentRevisionId == Guid.Empty)
        {
            throw new ArgumentException(
                "DocumentRevisionId must not be Guid.Empty.", nameof(documentRevisionId));
        }

        return await Query()
            .Where(c => c.DocumentRevisionId == documentRevisionId)
            .ToListAsync(cancellationToken);
    }
}
