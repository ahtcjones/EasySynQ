using EasySynQ.Data.Context;
using EasySynQ.Domain.Entities.Documents;
using EasySynQ.Services.Abstractions;

using Microsoft.EntityFrameworkCore;

namespace EasySynQ.Data.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IDocumentRepository"/>. Adds
/// <see cref="GetNonRetiredAsync"/> (ADR 0008 C6a) backing the Document
/// list view's default population.
/// </summary>
public sealed class DocumentRepository : Repository<Document, Guid>, IDocumentRepository
{
    /// <summary>Constructs the repository over the supplied DbContext.</summary>
    public DocumentRepository(EasySynQDbContext context)
        : base(context)
    {
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Document>> GetNonRetiredAsync(CancellationToken cancellationToken)
    {
        return await Query()
            .Where(d => d.RetiredAtUtc == null)
            .OrderBy(d => d.Number)
            .ToListAsync(cancellationToken);
    }
}
