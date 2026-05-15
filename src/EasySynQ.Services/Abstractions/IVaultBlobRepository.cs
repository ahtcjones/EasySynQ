using EasySynQ.Domain.Entities.Documents;

namespace EasySynQ.Services.Abstractions;

/// <summary>
/// Repository surface for <see cref="VaultBlob"/> rows (SPEC §3.6,
/// ADR 0008). Inherits the generic <see cref="IRepository{TEntity, TId}"/>
/// contract and adds a hash-lookup method — the only VaultBlob query
/// shape the service layer needs that's not expressible through the
/// generic <c>GetByIdAsync</c>.
/// </summary>
/// <remarks>
/// Hash lookup is the dedup-check path: <see cref="VaultService"/> hashes
/// incoming content, calls <see cref="FindByHashAsync"/>, and either
/// returns the existing blob (dedup hit) or proceeds to write a new row.
/// The unique index on <c>VaultBlobs.Sha256Hash</c> makes the lookup an
/// indexed single-row read. Lives on the repository (not inline in the
/// service) because the <c>EasySynQ.Services</c> project does not
/// reference EF Core directly per ADR 0003.
/// </remarks>
public interface IVaultBlobRepository : IRepository<VaultBlob, Guid>
{
    /// <summary>
    /// Returns the non-soft-deleted <see cref="VaultBlob"/> whose
    /// <see cref="VaultBlob.Sha256Hash"/> matches the supplied hash, or
    /// <see langword="null"/> if no match exists.
    /// </summary>
    /// <param name="sha256Hash">SHA-256 hash to match, in
    /// 64-character lowercase hexadecimal form.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matching blob row, or <see langword="null"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when
    /// <paramref name="sha256Hash"/> is null, empty, or whitespace.</exception>
    Task<VaultBlob?> FindByHashAsync(string sha256Hash, CancellationToken cancellationToken);
}
