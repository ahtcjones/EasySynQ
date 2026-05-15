using EasySynQ.Data.Context;
using EasySynQ.Domain.Entities.Documents;
using EasySynQ.Services.Abstractions;

namespace EasySynQ.Data.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IDocumentRepository"/>. Pure
/// pass-through over <see cref="Repository{TEntity, TId}"/>; the
/// dedicated subclass exists so future document-specific queries
/// (admin UI, list view models) can land here without disturbing the
/// generic contract (ADR 0008 C3).
/// </summary>
public sealed class DocumentRepository : Repository<Document, Guid>, IDocumentRepository
{
    /// <summary>Constructs the repository over the supplied DbContext.</summary>
    public DocumentRepository(EasySynQDbContext context)
        : base(context)
    {
    }
}
