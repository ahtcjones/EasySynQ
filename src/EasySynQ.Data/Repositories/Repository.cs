using EasySynQ.Data.Context;
using EasySynQ.Domain.Common;
using EasySynQ.Services.Abstractions;

using Microsoft.EntityFrameworkCore;

namespace EasySynQ.Data.Repositories;

/// <summary>
/// Generic EF Core implementation of <see cref="IRepository{TEntity, TId}"/>.
/// All entity-specific repositories derive from this and add their own
/// lookup methods or override mutation methods.
/// </summary>
public class Repository<TEntity, TId> : IRepository<TEntity, TId>
    where TEntity : class
{
    /// <summary>The shared DbContext for this scope.</summary>
    protected EasySynQDbContext Context { get; }

    /// <summary>Constructs the repository over the supplied DbContext.</summary>
    public Repository(EasySynQDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Context = context;
    }

    /// <inheritdoc />
    public virtual async Task<TEntity?> GetByIdAsync(TId id, CancellationToken cancellationToken)
    {
        // FindAsync bypasses query filters but checks the change tracker
        // first. We re-apply the soft-delete check here so the contract
        // matches what callers expect from a filter-respecting fetch.
        var entity = await Context.Set<TEntity>().FindAsync(keyValues: [id!], cancellationToken: cancellationToken);
        if (entity is AuditableEntity { IsDeleted: true })
        {
            return null;
        }
        return entity;
    }

    /// <inheritdoc />
    public virtual async Task<TEntity?> GetByIdIncludingDeletedAsync(TId id, CancellationToken cancellationToken)
    {
        // FindAsync in EF Core 10 applies query filters, so it would not
        // return soft-deleted rows even though it bypasses the LINQ
        // pipeline for the primary-key lookup. Explicitly going through
        // IgnoreQueryFilters() + IQueryable is the way to opt out.
        // EF.Property<TId>(e, keyName).Equals(id) is the unconstrained-
        // generic key-equality idiom EF Core translates to SQL "=".
        var keyName = Context.Model.FindEntityType(typeof(TEntity))!
            .FindPrimaryKey()!
            .Properties[0]
            .Name;

        return await Context.Set<TEntity>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => EF.Property<TId>(e, keyName)!.Equals(id), cancellationToken);
    }

    /// <inheritdoc />
    public virtual Task<bool> AnyAsync(CancellationToken cancellationToken)
        => Context.Set<TEntity>().AnyAsync(cancellationToken);

    /// <inheritdoc />
    public virtual IQueryable<TEntity> Query() => Context.Set<TEntity>();

    /// <inheritdoc />
    public virtual IQueryable<TEntity> QueryIncludingDeleted() => Context.Set<TEntity>().IgnoreQueryFilters();

    /// <inheritdoc />
    public virtual async Task AddAsync(TEntity entity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entity);
        await Context.Set<TEntity>().AddAsync(entity, cancellationToken);
    }

    /// <inheritdoc />
    public virtual Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        => Context.SaveChangesAsync(cancellationToken);

    /// <inheritdoc />
    public virtual void SoftDelete(TEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        if (entity is not AuditableEntity)
        {
            throw new InvalidOperationException(
                $"SoftDelete is not supported for {typeof(TEntity).Name}; the type does not inherit AuditableEntity. " +
                $"Entities without an IsDeleted column (notably AuditLogEntry per SPEC §3.4) cannot be soft-deleted.");
        }

        // Set via Entry API since IsDeleted has a protected setter on the
        // domain entity. The audit interceptor detects the false → true
        // transition and emits an AuditAction.Delete row on save.
        Context.Entry(entity).Property(nameof(AuditableEntity.IsDeleted)).CurrentValue = true;
    }

    /// <inheritdoc />
    public virtual void HardDelete(TEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        Context.Set<TEntity>().Remove(entity);
    }
}
