using EasySynQ.Data.Context;
using EasySynQ.Domain.Entities.Documents;
using EasySynQ.Services.Abstractions;

using Microsoft.EntityFrameworkCore;

namespace EasySynQ.Data.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IVaultBlobRepository"/>. Extends
/// the generic <see cref="Repository{TEntity, TId}"/> with the
/// hash-lookup query the vault service uses to detect duplicate content
/// before writing a new row.
/// </summary>
public sealed class VaultBlobRepository : Repository<VaultBlob, Guid>, IVaultBlobRepository
{
    /// <summary>Constructs the repository over the supplied DbContext.</summary>
    public VaultBlobRepository(EasySynQDbContext context)
        : base(context)
    {
    }

    /// <inheritdoc />
    public async Task<VaultBlob?> FindByHashAsync(string sha256Hash, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256Hash);

        // The Sha256Hash unique index makes this an indexed single-row
        // read. Soft-delete filter applies via Query(); a soft-deleted
        // row with this hash returns null, so subsequent vault writes
        // of the same content would be treated as new (the unique index
        // still applies to soft-deleted rows because the filter is
        // query-time, not DB-time — but the operational soft-delete
        // workflow for VaultBlobs isn't a Phase 2 concern, so this is
        // a future-cleanup note rather than a Phase 2 bug).
        return await Query()
            .FirstOrDefaultAsync(b => b.Sha256Hash == sha256Hash, cancellationToken);
    }
}
