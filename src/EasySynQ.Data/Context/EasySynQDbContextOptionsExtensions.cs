using EasySynQ.Data.Interceptors;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EasySynQ.Data.Context;

/// <summary>
/// Wires both EasySynQ data-layer interceptors onto a
/// <see cref="DbContextOptionsBuilder"/> in the order that
/// <see cref="StandardFieldsInterceptor"/> sees each save first and
/// <see cref="AuditSaveChangesInterceptor"/> sees it after the standard
/// fields have been stamped.
/// </summary>
public static class EasySynQDbContextOptionsExtensions
{
    /// <summary>
    /// Resolves both interceptors from the supplied
    /// <see cref="IServiceProvider"/> and registers them on the options
    /// builder. The order — standard fields first, audit second — is
    /// load-bearing and is enforced here.
    /// </summary>
    /// <param name="optionsBuilder">The options builder being configured.</param>
    /// <param name="serviceProvider">DI container that has both
    /// interceptors and their dependencies (<see cref="IClock"/>,
    /// <see cref="ICurrentUserAccessor"/>,
    /// <see cref="IAuditCorrelationProvider"/>) registered.</param>
    /// <returns>The same <paramref name="optionsBuilder"/> for chaining.</returns>
    public static DbContextOptionsBuilder UseEasySynQInterceptors(
        this DbContextOptionsBuilder optionsBuilder,
        IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        var standardFields = serviceProvider.GetRequiredService<StandardFieldsInterceptor>();
        var audit = serviceProvider.GetRequiredService<AuditSaveChangesInterceptor>();

        // Order matters. EF Core invokes interceptors in registration order
        // for SavingChangesAsync, so StandardFieldsInterceptor stamps audit
        // fields before AuditSaveChangesInterceptor reads CurrentValues.
        optionsBuilder.AddInterceptors(standardFields, audit);

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
