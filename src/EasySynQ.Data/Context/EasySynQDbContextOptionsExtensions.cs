using EasySynQ.Data.Interceptors;
using EasySynQ.Services.Events;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EasySynQ.Data.Context;

/// <summary>
/// Wires the EasySynQ data-layer interceptors onto a
/// <see cref="DbContextOptionsBuilder"/> in the order:
/// <list type="number">
///   <item><see cref="DomainEventDispatchInterceptor"/> — drains the
///   per-scope event queue so handler-staged entities pick up
///   subsequent stamping/audit-row generation.</item>
///   <item><see cref="StandardFieldsInterceptor"/> — stamps audit
///   fields on every modified entity (including handler-added).</item>
///   <item><see cref="AuditSaveChangesInterceptor"/> — reads the
///   change tracker and emits one <c>AuditLogEntry</c> per touched
///   compliance-critical entity (including handler-added).</item>
/// </list>
/// </summary>
public static class EasySynQDbContextOptionsExtensions
{
    /// <summary>
    /// Resolves all interceptors from the supplied
    /// <see cref="IServiceProvider"/> and registers them on the options
    /// builder. The dispatch → standard-fields → audit ordering is
    /// load-bearing and is enforced here (ADR 0008 C3).
    /// </summary>
    /// <param name="optionsBuilder">The options builder being configured.</param>
    /// <param name="serviceProvider">DI container that has the three
    /// interceptors and their dependencies (<see cref="IClock"/>,
    /// <see cref="ICurrentUserAccessor"/>,
    /// <see cref="IAuditCorrelationProvider"/>,
    /// <see cref="IDomainEventDispatcher"/>) registered.</param>
    /// <returns>The same <paramref name="optionsBuilder"/> for chaining.</returns>
    public static DbContextOptionsBuilder UseEasySynQInterceptors(
        this DbContextOptionsBuilder optionsBuilder,
        IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        var dispatch = serviceProvider.GetRequiredService<DomainEventDispatchInterceptor>();
        var standardFields = serviceProvider.GetRequiredService<StandardFieldsInterceptor>();
        var audit = serviceProvider.GetRequiredService<AuditSaveChangesInterceptor>();

        // Order matters. EF Core invokes interceptors in registration
        // order for SavingChangesAsync. Dispatch runs first so handler-
        // staged entities receive standard-fields stamping (next) and
        // audit-row generation (last) as part of the same SaveChanges.
        optionsBuilder.AddInterceptors(dispatch, standardFields, audit);

        return optionsBuilder;
    }

    /// <summary>
    /// Generic overload that preserves the <typeparamref name="TContext"/>
    /// type through fluent chains like
    /// <c>new DbContextOptionsBuilder&lt;EasySynQDbContext&gt;()
    ///     .UseSqlite(...).UseEasySynQInterceptors(sp).Options</c>.
    /// Without this overload, the non-generic version above is selected
    /// and the chain returns a non-generic <see cref="DbContextOptions"/>.
    /// </summary>
    public static DbContextOptionsBuilder<TContext> UseEasySynQInterceptors<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        IServiceProvider serviceProvider)
        where TContext : DbContext
    {
        UseEasySynQInterceptors((DbContextOptionsBuilder)optionsBuilder, serviceProvider);
        return optionsBuilder;
    }
}
