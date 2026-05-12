using EasySynQ.Data.Context;
using EasySynQ.Tests.TestHelpers;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace EasySynQ.Tests.Integration.Data.Interceptors;

/// <summary>
/// Base class for integration tests that exercise the data-layer
/// interceptor pipeline (<c>StandardFieldsInterceptor</c> +
/// <c>AuditSaveChangesInterceptor</c>) and/or the effective-dating
/// query filter. Each test gets its own SQLite database and a fresh set
/// of mutable test doubles so it can adjust the clock, user, correlation
/// id, or "as of" instant independently.
/// </summary>
public abstract class InterceptorIntegrationTestBase : IDisposable
{
    private readonly string _dbPath;
    private bool _disposed;

    /// <summary>Mutable clock; tests may set <c>Clock.UtcNow</c>.</summary>
    protected FixedClock Clock { get; }

    /// <summary>Mutable current-user accessor; default unauthenticated.</summary>
    protected MutableCurrentUserAccessor CurrentUser { get; }

    /// <summary>Mutable correlation provider; default null (per-save fallback).</summary>
    protected MutableAuditCorrelationProvider Correlation { get; }

    /// <summary>Mutable temporal resolver shared across all
    /// <see cref="NewContext()"/> calls in this test.</summary>
    protected MutableTemporalResolver TemporalResolver { get; }

    /// <summary>Options bound to this test's temp database with
    /// interceptors wired.</summary>
    protected DbContextOptions<EasySynQDbContext> Options { get; }

    /// <summary>DI container containing the test doubles and both
    /// interceptors.</summary>
    protected IServiceProvider Services { get; }

    /// <summary>Construct a fresh database and DI graph for this test.</summary>
    protected InterceptorIntegrationTestBase()
    {
        var startInstant = new DateTime(2026, 5, 12, 12, 0, 0, DateTimeKind.Utc);

        Clock = new FixedClock(startInstant);
        CurrentUser = new MutableCurrentUserAccessor();
        Correlation = new MutableAuditCorrelationProvider();
        TemporalResolver = new MutableTemporalResolver(startInstant);

        (_dbPath, Options, Services) = TempSqliteDb.CreateWithInterceptors(
            Clock, CurrentUser, Correlation, TemporalResolver);
    }

    /// <summary>Creates a new DbContext bound to this test's database +
    /// the mutable temporal resolver.</summary>
    protected EasySynQDbContext NewContext() => new(Options, TemporalResolver);

    /// <summary>Current test's cancellation token (xUnit v3 cancellable
    /// async pattern).</summary>
    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Cleans up the temp database file.</summary>
    /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            (Services as IDisposable)?.Dispose();
            TempSqliteDb.Delete(_dbPath);
        }

        _disposed = true;
    }
}
