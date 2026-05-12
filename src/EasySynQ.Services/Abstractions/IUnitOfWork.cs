namespace EasySynQ.Services.Abstractions;

/// <summary>
/// Coordinates persistence across multiple repositories sharing the same
/// <c>DbContext</c>, and provides an explicit transaction scope for the
/// rare case where multiple <c>SaveChanges</c> calls must succeed or fail
/// together.
/// </summary>
/// <remarks>
/// A single <see cref="SaveChangesAsync"/> call is already atomic per
/// EF Core's default behavior — every <c>SaveChanges</c> is wrapped in an
/// implicit transaction. <see cref="ExecuteInTransactionAsync"/> is for
/// the genuinely multi-step case where two or more <c>SaveChanges</c>
/// calls (often interleaved with reads that must see prior writes) must
/// be a single atomic unit. Reach for it sparingly; a single
/// <c>SaveChangesAsync</c> over a richer change set is almost always the
/// right answer.
/// </remarks>
public interface IUnitOfWork
{
    /// <summary>
    /// Saves all pending changes on the underlying <c>DbContext</c>.
    /// Equivalent to calling <see cref="IRepository{TEntity, TId}.SaveChangesAsync"/>
    /// on any of the registered repositories — they all share one
    /// context, so one save commits everything tracked.
    /// </summary>
    /// <returns>Number of state entries affected.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Runs <paramref name="work"/> inside a database transaction. If
    /// <paramref name="work"/> completes without throwing, the
    /// transaction commits. If it throws (or
    /// <paramref name="cancellationToken"/> fires), the transaction
    /// rolls back and the exception propagates.
    /// </summary>
    /// <remarks>
    /// The <paramref name="work"/> delegate is responsible for calling
    /// <see cref="SaveChangesAsync"/> (or repository
    /// <c>SaveChangesAsync</c>) at the appropriate points — opening the
    /// transaction does NOT auto-save.
    /// </remarks>
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken);
}
