using EasySynQ.Domain.Entities.Documents;

namespace EasySynQ.Services.Abstractions;

/// <summary>
/// Repository surface for <see cref="Document"/> rows (ADR 0008 C3).
/// Inherits the generic <see cref="IRepository{TEntity, TId}"/>; today
/// the lifecycle service consumes only <c>GetByIdAsync</c> /
/// <c>SaveChangesAsync</c> and the in-line query surface, so no
/// entity-specific methods are added yet. The dedicated interface
/// exists so future phases (admin UI, document list view models, etc.)
/// can add document-specific queries here without disturbing the
/// generic repository contract.
/// </summary>
public interface IDocumentRepository : IRepository<Document, Guid>
{
}
