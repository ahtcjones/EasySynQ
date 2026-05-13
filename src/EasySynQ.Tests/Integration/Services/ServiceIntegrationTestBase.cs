using EasySynQ.Data.Context;
using EasySynQ.Data.Interceptors;
using EasySynQ.Data.Repositories;
using EasySynQ.Services.Abstractions;
using EasySynQ.Services.Audit;
using EasySynQ.Services.Identity;
using EasySynQ.Services.Signatures;
using EasySynQ.Services.Time;
using EasySynQ.Tests.TestHelpers;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace EasySynQ.Tests.Integration.Services;

/// <summary>
/// Base for integration tests of services in <c>EasySynQ.Services</c>
/// (auth, signatures) that need a real <c>DbContext</c> + interceptor
/// pipeline behind a real repository + unit-of-work surface. Builds on
/// the same temp-SQLite-per-test pattern the data-layer tests use, but
/// wires the full DI graph so tests resolve services exactly as the
/// production host would.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a separate base from
/// <see cref="EasySynQ.Tests.Integration.Data.Interceptors.InterceptorIntegrationTestBase"/>:</b>
/// the data-layer base wires the four cross-cutting test doubles plus the
/// interceptors, but does not register repositories, the unit of work,
/// or the auth/signature services — those are exactly the things the
/// service-layer tests need to resolve. Adding them to the data-layer
/// base would force every interceptor test to drag along an unused DI
/// graph.
/// </para>
/// <para>
/// <b>Test policy.</b> The
/// <see cref="IPasswordPolicy"/> registered here is intentionally
/// weakened (<c>MinimumLength = 4</c>, <c>CurrentIterationCount = 1000</c>)
/// so PBKDF2 verifies finish in microseconds. Tests that need to
/// exercise the production iteration count construct their own
/// <see cref="PasswordHasher"/> with the production-default
/// <see cref="PasswordPolicy"/>; that path runs separately and slowly.
/// Lockout policy values (<c>MaxFailedAttempts = 5</c>,
/// <c>LockoutDuration = 15 min</c>) match production so the lockout
/// tests are realistic; tests advance the
/// <see cref="FixedClock"/> rather than waiting wall-clock time.
/// </para>
/// </remarks>
public abstract class ServiceIntegrationTestBase : IDisposable
{
    /// <summary>Fast test policy — see class remarks.</summary>
    public const int TestMinimumLength = 4;

    /// <summary>Fast test PBKDF2 iteration count — see class remarks.</summary>
    public const int TestIterationCount = 1000;

    private readonly string _dbPath;
    private bool _disposed;

    /// <summary>Mutable clock; tests may set <c>Clock.UtcNow</c>.</summary>
    protected FixedClock Clock { get; }

    /// <summary>Mutable current-user accessor; default unauthenticated.</summary>
    protected MutableCurrentUserAccessor CurrentUser { get; }

    /// <summary>Mutable correlation provider; default null (per-save fallback).</summary>
    protected MutableAuditCorrelationProvider Correlation { get; }

    /// <summary>Mutable temporal resolver shared across all
    /// <see cref="NewContext()"/> calls.</summary>
    protected MutableTemporalResolver TemporalResolver { get; }

    /// <summary>The fast test policy (registered as singleton in
    /// <see cref="ServiceProvider"/>).</summary>
    protected IPasswordPolicy Policy { get; }

    /// <summary>Options bound to this test's temp database with
    /// interceptors wired.</summary>
    protected DbContextOptions<EasySynQDbContext> Options { get; }

    /// <summary>Root DI container — resolve scopes off it for each unit
    /// of work in a test.</summary>
    protected IServiceProvider ServiceProvider { get; }

    /// <summary>Construct a fresh database and DI graph for this test.</summary>
    protected ServiceIntegrationTestBase()
    {
        var startInstant = new DateTime(2026, 5, 12, 12, 0, 0, DateTimeKind.Utc);

        Clock = new FixedClock(startInstant);
        CurrentUser = new MutableCurrentUserAccessor();
        Correlation = new MutableAuditCorrelationProvider();
        TemporalResolver = new MutableTemporalResolver(startInstant);
        Policy = new PasswordPolicy(
            minimumLength: TestMinimumLength,
            currentIterationCount: TestIterationCount,
            maxFailedAttempts: PasswordPolicy.DefaultMaxFailedAttempts,
            lockoutDuration: PasswordPolicy.DefaultLockoutDuration);

        _dbPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"easysynq-svc-{Guid.NewGuid():N}.db");

        var services = new ServiceCollection();

        // Cross-cutting abstractions — the test doubles. Singletons so
        // every scope sees the same instance (and therefore the same
        // mutated values when a test sets, e.g., CurrentUser.UserId).
        services.AddSingleton<IClock>(Clock);
        services.AddSingleton<ICurrentUserAccessor>(CurrentUser);
        services.AddSingleton<IAuditCorrelationProvider>(Correlation);
        services.AddSingleton<ITemporalResolver>(TemporalResolver);

        // Interceptors — scoped per the production registration.
        services.AddScoped<StandardFieldsInterceptor>();
        services.AddScoped<AuditSaveChangesInterceptor>();

        // Identity & signature services.
        services.AddSingleton(Policy);
        services.AddSingleton<IPasswordHasher>(new PasswordHasher(Policy));
        services.AddScoped<IAuthenticationService, AuthenticationService>();
        services.AddScoped<ISignatureService, SignatureService>();

        // We need a temporary singleton-only provider to resolve the
        // interceptors when building DbContextOptions. The interceptors
        // are scoped, but we want the options to capture them via the
        // production code path (UseEasySynQInterceptors). The cleanest
        // way is to first build a provider with just the singletons +
        // interceptors-as-singletons (override scope), construct the
        // options, then register the options bag for downstream scoped
        // DbContext resolution.
        var interceptorPrep = new ServiceCollection();
        interceptorPrep.AddSingleton<IClock>(Clock);
        interceptorPrep.AddSingleton<ICurrentUserAccessor>(CurrentUser);
        interceptorPrep.AddSingleton<IAuditCorrelationProvider>(Correlation);
        interceptorPrep.AddSingleton<StandardFieldsInterceptor>();
        interceptorPrep.AddSingleton<AuditSaveChangesInterceptor>();
        var interceptorProvider = interceptorPrep.BuildServiceProvider();

        Options = new DbContextOptionsBuilder<EasySynQDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .UseEasySynQInterceptors(interceptorProvider)
            .Options;

        // Register the DbContext as scoped, constructed from the
        // pre-built Options + the shared temporal resolver. This is the
        // same shape `AddDbContext` produces but with explicit options
        // we control.
        var capturedOptions = Options;
        services.AddScoped(sp => new EasySynQDbContext(
            capturedOptions,
            sp.GetRequiredService<ITemporalResolver>()));

        // Repositories + UoW (post DbContext registration so resolution works).
        services.AddScoped(typeof(IRepository<,>), typeof(Repository<,>));
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        ServiceProvider = services.BuildServiceProvider();

        // Migrate the schema once per test instance.
        using (var ctx = NewContext())
        {
            ctx.Database.Migrate();
        }
    }

    /// <summary>
    /// Creates a new DbContext bound to this test's database and the
    /// shared temporal resolver — for tests that want to bypass DI and
    /// query the DB directly (audit-log inspection, etc.). The returned
    /// context has the interceptor pipeline wired.
    /// </summary>
    protected EasySynQDbContext NewContext() => new(Options, TemporalResolver);

    /// <summary>
    /// Creates a fresh DI scope. Tests use one scope per logical
    /// operation so each operation runs against its own
    /// <see cref="EasySynQDbContext"/> instance — matching the
    /// production WPF flow where each command is a scope. Returns
    /// <see cref="AsyncServiceScope"/> so callers may
    /// <c>await using</c> the scope (the underlying
    /// <see cref="EasySynQDbContext"/> implements
    /// <see cref="IAsyncDisposable"/>).
    /// </summary>
    protected AsyncServiceScope NewScope() => ServiceProvider.CreateAsyncScope();

    /// <summary>Current test's cancellation token.</summary>
    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Cleans up the DI container and the temp database file.</summary>
    /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            (ServiceProvider as IDisposable)?.Dispose();
            TempSqliteDb.Delete(_dbPath);
        }

        _disposed = true;
    }
}
