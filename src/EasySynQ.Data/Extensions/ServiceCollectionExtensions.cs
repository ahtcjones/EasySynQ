using EasySynQ.Data.Context;
using EasySynQ.Data.Interceptors;
using EasySynQ.Data.Repositories;
using EasySynQ.Services.Abstractions;
using EasySynQ.Services.Bootstrap;
using EasySynQ.Services.Identity;
using EasySynQ.Services.Signatures;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EasySynQ.Data.Extensions;

/// <summary>
/// DI registration helpers for EasySynQ's data layer.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the DbContext, both interceptors, the generic repository,
    /// the concrete repositories, and the unit of work. Does NOT register
    /// the cross-cutting abstractions (<see cref="EasySynQ.Services.Time.IClock"/>,
    /// <see cref="ICurrentUserAccessor"/>,
    /// <see cref="EasySynQ.Services.Audit.IAuditCorrelationProvider"/>,
    /// <see cref="ITemporalResolver"/>) — those are host-specific and the
    /// caller registers them before this method runs.
    /// </summary>
    /// <example>
    /// Typical host wiring (WPF App startup, or a test fixture):
    /// <code>
    /// // Step 1: cross-cutting abstractions, host-chosen implementations
    /// services.AddSingleton&lt;IClock, SystemClock&gt;();
    /// services.AddScoped&lt;ICurrentUserAccessor, WpfCurrentUserAccessor&gt;();
    /// services.AddScoped&lt;IAuditCorrelationProvider, UiCorrelationProvider&gt;();
    /// services.AddScoped&lt;ITemporalResolver, CurrentTimeTemporalResolver&gt;();
    ///
    /// // Step 2: data layer infrastructure
    /// services.AddEasySynQDataServices("Data Source=Q:/EasySynQ/db/EasySynQ_Master.db");
    /// </code>
    /// </example>
    /// <param name="services">The service collection being configured.</param>
    /// <param name="connectionString">SQLite connection string for the
    /// <see cref="EasySynQDbContext"/>.</param>
    /// <returns><paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddEasySynQDataServices(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        // Interceptors. Scoped because they consume scoped abstractions
        // (ICurrentUserAccessor, IAuditCorrelationProvider).
        services.AddScoped<StandardFieldsInterceptor>();
        services.AddScoped<AuditSaveChangesInterceptor>();

        // DbContext. The factory receives an IServiceProvider so
        // UseEasySynQInterceptors can resolve the two interceptor
        // services. ITemporalResolver is injected by EF Core's
        // constructor-binding from the same provider.
        services.AddDbContext<EasySynQDbContext>((sp, options) =>
        {
            options.UseSqlite(connectionString);
            options.UseEasySynQInterceptors(sp);
        });

        // Generic repository — used directly for entities that need no
        // entity-specific methods, or as the base of concrete repos.
        services.AddScoped(typeof(IRepository<,>), typeof(Repository<,>));

        // Concrete repositories with entity-specific methods.
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUserRoleRepository, UserRoleRepository>();
        services.AddScoped<IPermissionRepository, PermissionRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();

        // Unit of work for explicit cross-save transactions.
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // Identity & signature services (Chunk D / ADR 0006).
        // Policy as a singleton: thread-safe immutable values.
        // Hasher as a singleton: stateless beyond the policy.
        // AuthenticationService as scoped: holds the per-instance dummy
        // hash cache and is composed with scoped repositories.
        // SignatureService as scoped: composes with scoped repositories
        // and the scoped current-user accessor.
        services.AddSingleton<IPasswordPolicy, PasswordPolicy>();
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddScoped<IAuthenticationService, AuthenticationService>();
        services.AddScoped<IBootstrapService, BootstrapService>();
        services.AddScoped<ISignatureService, SignatureService>();

        return services;
    }
}
