using EasySynQ.Data.Context;
using EasySynQ.Domain.Entities.Audit;
using EasySynQ.Services.Abstractions;

using Microsoft.EntityFrameworkCore;

namespace EasySynQ.Data.Repositories;

/// <summary>
/// EF Core implementation of <see cref="ILockReasonRepository"/>
/// (ADR 0012 C7a). Adds the
/// <c>(LockedEntityType, LockedEntityId)</c> lookup used by the
/// lock-inspector view-model's cache-first read path.
/// </summary>
public sealed class LockReasonRepository
    : Repository<LockReason, Guid>, ILockReasonRepository
{
    /// <summary>Constructs the repository over the supplied DbContext.</summary>
    public LockReasonRepository(EasySynQDbContext context)
        : base(context)
    {
    }

    /// <inheritdoc />
    public async Task<LockReason?> GetByLockedEntityAsync(
        string lockedEntityType,
        string lockedEntityId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockedEntityType);
        ArgumentException.ThrowIfNullOrWhiteSpace(lockedEntityId);

        return await Query()
            .SingleOrDefaultAsync(
                lr => lr.LockedEntityType == lockedEntityType
                   && lr.LockedEntityId == lockedEntityId,
                cancellationToken);
    }
}
