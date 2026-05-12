using EasySynQ.Data.Context;
using EasySynQ.Services.Abstractions;

namespace EasySynQ.Data.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IUnitOfWork"/>. Delegates to the
/// shared <see cref="EasySynQDbContext"/> for both saves and explicit
/// transactions.
/// </summary>
public class UnitOfWork : IUnitOfWork
{
    private readonly EasySynQDbContext _context;

    /// <summary>Constructs the unit of work over the supplied DbContext.</summary>
    public UnitOfWork(EasySynQDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <inheritdoc />
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        => _context.SaveChangesAsync(cancellationToken);

    /// <inheritdoc />
    public async Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> work,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await work(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
