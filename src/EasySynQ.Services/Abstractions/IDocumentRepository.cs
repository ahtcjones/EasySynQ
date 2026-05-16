using EasySynQ.Domain.Entities.Documents;

namespace EasySynQ.Services.Abstractions;

/// <summary>
/// Repository surface for <see cref="Document"/> rows (ADR 0008 C3 / C6a).
/// Inherits the generic <see cref="IRepository{TEntity, TId}"/>; the C6a
/// addition (<see cref="GetNonRetiredAsync"/>) backs the Document list
/// view's default population. The dedicated interface remains so future
/// phases can add document-specific queries here without disturbing the
/// generic repository contract.
/// </summary>
public interface IDocumentRepository : IRepository<Document, Guid>
{
    /// <summary>
    /// Returns every non-retired, non-soft-deleted
    /// <see cref="Document"/>, ordered by <see cref="Document.Number"/>
    /// ascending. Backs the C6a Document list view's default population.
    /// Retired documents (where <see cref="Document.RetiredAtUtc"/> is
    /// non-null) are excluded; the View-Archived filter that exposes
    /// them lands in a later commit per ADR 0008's chunking.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Naming note.</b> <see cref="Document"/> is internal-only by
    /// design — external specifications (ASTM, AMS, customer specs)
    /// are modeled by the separate <c>ExternalDocument</c> entity. There
    /// is no <c>Library</c> discriminator on <see cref="Document"/>, so
    /// the method name reflects the actual filter (non-retired) rather
    /// than implying a library distinction that does not exist at this
    /// type.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Non-retired documents ordered by Number ascending.</returns>
    Task<IReadOnlyList<Document>> GetNonRetiredAsync(CancellationToken cancellationToken);
}
