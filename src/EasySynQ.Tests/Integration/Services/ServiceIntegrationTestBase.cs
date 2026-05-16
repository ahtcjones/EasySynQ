using EasySynQ.Data.Context;
using EasySynQ.Data.Interceptors;
using EasySynQ.Data.Repositories;
using EasySynQ.Domain.Entities.Identity;
using EasySynQ.Services.Abstractions;
using EasySynQ.Services.Audit;
using EasySynQ.Services.Bootstrap;
using EasySynQ.Services.Documents;
using EasySynQ.Services.Events;
using EasySynQ.Services.Identity;
using EasySynQ.Services.Signatures;
using EasySynQ.Services.Time;
using EasySynQ.Services.Vault;
using EasySynQ.Tests.TestHelpers;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
    private readonly string _vaultRoot;
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

    /// <summary>
    /// Recording domain event dispatcher shared across the prep
    /// container (interceptor binding) and the runtime container
    /// (lifecycle service injection). Tests inspect
    /// <c>EventDispatcher.Recorded</c> to assert publication. See
    /// <see cref="RecordingDomainEventDispatcher"/> remarks for the
    /// singleton-in-tests-scoped-in-prod tradeoff.
    /// </summary>
    protected RecordingDomainEventDispatcher EventDispatcher { get; }

    /// <summary>The fast test policy (registered as singleton in
    /// <see cref="ServiceProvider"/>).</summary>
    protected IPasswordPolicy Policy { get; }

    /// <summary>Options bound to this test's temp database with
    /// interceptors wired.</summary>
    protected DbContextOptions<EasySynQDbContext> Options { get; }

    /// <summary>Root DI container — resolve scopes off it for each unit
    /// of work in a test.</summary>
    protected IServiceProvider ServiceProvider { get; }

    /// <summary>
    /// Absolute path to this test's vault root tempdir. Tests that
    /// poke directly at on-disk vault state (corruption-injection,
    /// orphan-file checks) read this. The directory is created on
    /// construction and cleaned up on dispose.
    /// </summary>
    protected string VaultRoot => _vaultRoot;

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

        _vaultRoot = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"easysynq-vault-{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(_vaultRoot);

        var services = new ServiceCollection();

        // Cross-cutting abstractions — the test doubles. Singletons so
        // every scope sees the same instance (and therefore the same
        // mutated values when a test sets, e.g., CurrentUser.UserId).
        services.AddSingleton<IClock>(Clock);
        services.AddSingleton<ICurrentUserAccessor>(CurrentUser);
        services.AddSingleton<IAuditCorrelationProvider>(Correlation);
        services.AddSingleton<ITemporalResolver>(TemporalResolver);

        // Interceptors — scoped per the production registration.
        // (DomainEventDispatchInterceptor is registered as a singleton
        // below alongside the shared dispatcher so the captured-options
        // and the runtime container reference the same instance.)
        services.AddScoped<StandardFieldsInterceptor>();
        services.AddScoped<AuditSaveChangesInterceptor>();

        // ILogger<T> for services that emit structured logs
        // (BootstrapService — first Services consumer). NullLogger
        // backing under default registration; tests don't assert on
        // log output but the DI graph must resolve.
        services.AddLogging();

        // Identity, bootstrap & signature services.
        services.AddSingleton(Policy);
        services.AddSingleton<IPasswordHasher>(new PasswordHasher(Policy));
        services.AddScoped<IAuthenticationService, AuthenticationService>();
        services.AddScoped<IBootstrapService, BootstrapService>();
        services.AddScoped<ISignatureService, SignatureService>();

        // Vault services (ADR 0008 C2). Path provider is singleton with
        // a per-test tempdir root; the vault service consumes it
        // alongside the scoped IVaultBlobRepository + IUnitOfWork.
        services.AddSingleton<IVaultPathProvider>(new FixedVaultPathProvider(_vaultRoot));
        services.AddScoped<IVaultService, VaultService>();

        // Lifecycle service (ADR 0008 C3) — depends on the
        // singleton dispatcher registered immediately below.
        services.AddScoped<IDocumentLifecycleService, DocumentLifecycleService>();

        // We need a temporary singleton-only provider to resolve the
        // interceptors when building DbContextOptions. The interceptors
        // are scoped, but we want the options to capture them via the
        // production code path (UseEasySynQInterceptors). The cleanest
        // way is to first build a provider with just the singletons +
        // interceptors-as-singletons (override scope), construct the
        // options, then register the options bag for downstream scoped
        // DbContext resolution.
        //
        // ADR 0008 C3 addition — the dispatcher and the dispatch
        // interceptor must share an instance with the runtime
        // lifecycle service. Build the dispatcher once in the prep
        // container, then register the SAME instance as a singleton
        // in the runtime container below. The dispatcher's
        // IServiceProvider is the prep container — Phase 2 has no
        // event handlers in production, and tests verify publication
        // via the recording wrapper rather than by registering
        // handlers in DI.
        var interceptorPrep = new ServiceCollection();
        interceptorPrep.AddSingleton<IClock>(Clock);
        interceptorPrep.AddSingleton<ICurrentUserAccessor>(CurrentUser);
        interceptorPrep.AddSingleton<IAuditCorrelationProvider>(Correlation);
        interceptorPrep.AddSingleton<StandardFieldsInterceptor>();
        interceptorPrep.AddSingleton<AuditSaveChangesInterceptor>();
        // Dispatcher built later (after the prep provider exists, so
        // the dispatcher's serviceProvider is wired); registered into
        // both containers as the singleton instance.
        var interceptorProvider = interceptorPrep.BuildServiceProvider();
        EventDispatcher = new RecordingDomainEventDispatcher(interceptorProvider);

        // Register the dispatcher in BOTH containers as a singleton
        // pointing at the same instance. The interceptor (resolved
        // from the prep container at options-build time) and the
        // lifecycle service (resolved from the runtime container at
        // service-call time) share the queue.
        var dispatchInterceptor = new DomainEventDispatchInterceptor(EventDispatcher);
        var dispatchInterceptorBox = dispatchInterceptor;
        services.AddSingleton<IDomainEventDispatcher>(EventDispatcher);
        services.AddSingleton(dispatchInterceptorBox);

        // Manual interceptor wiring rather than UseEasySynQInterceptors
        // so we can interleave the singleton dispatch interceptor (held
        // outside the prep container) in the right position. Order
        // matches production: dispatch → standard-fields → audit.
        Options = new DbContextOptionsBuilder<EasySynQDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .AddInterceptors(
                dispatchInterceptor,
                interceptorProvider.GetRequiredService<StandardFieldsInterceptor>(),
                interceptorProvider.GetRequiredService<AuditSaveChangesInterceptor>())
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
        services.AddScoped<IUserRoleRepository, UserRoleRepository>();
        services.AddScoped<IPermissionRepository, PermissionRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<IVaultBlobRepository, VaultBlobRepository>();
        services.AddScoped<IDocumentRepository, DocumentRepository>();
        services.AddScoped<IDocumentRevisionRepository, DocumentRevisionRepository>();
        services.AddScoped<IDocumentReviewAssignmentRepository, DocumentReviewAssignmentRepository>();
        services.AddScoped<IDocumentReviewCommentRepository, DocumentReviewCommentRepository>();
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

    /// <summary>
    /// Seeds a single user with the supplied plaintext password hashed
    /// under the test <see cref="Policy"/>. Returns the new user's id.
    /// Shared seeding helper for service-tier tests that need a
    /// pre-existing user (auth + bootstrap suites both consume it).
    /// </summary>
    /// <param name="username">Username; used for both the
    /// <see cref="User.Username"/> and <see cref="User.DisplayName"/>
    /// fields.</param>
    /// <param name="password">Plaintext password to hash.</param>
    /// <returns>The persisted user's id.</returns>
    protected async Task<Guid> SeedUserAsync(string username, string password)
    {
        var hasher = new PasswordHasher(Policy);
        var hashed = hasher.Hash(password);
        var userId = Guid.NewGuid();
        await using var ctx = NewContext();
        ctx.Users.Add(new User(
            userId, username, username,
            hashed.Hash, hashed.Salt, hashed.IterationCount,
            mustChangePassword: false));
        await ctx.SaveChangesAsync(Ct);
        return userId;
    }

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

            try
            {
                if (System.IO.Directory.Exists(_vaultRoot))
                {
                    System.IO.Directory.Delete(_vaultRoot, recursive: true);
                }
            }
            catch (System.IO.IOException)
            {
                // Best-effort cleanup; per-test vault roots use GUID
                // suffixes so a stale dir doesn't break isolation.
            }
        }

        _disposed = true;
    }
}
