using EasySynQ.Data.Context;
using EasySynQ.Services.Abstractions;
using EasySynQ.Services.Time;
using EasySynQ.Tests.TestHelpers;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace EasySynQ.Tests.Integration.Data;

/// <summary>
/// Base class for Data-layer integration tests that do NOT exercise the
/// interceptor pipeline. Creates a fresh temp SQLite database per test
/// (xUnit instantiates a new test class per test method), applies
/// migrations, and cleans up on Dispose.
/// </summary>
/// <remarks>
/// Interceptor-aware tests should derive from
/// <c>InterceptorIntegrationTestBase</c> instead, which wires the audit
/// and standard-fields interceptors with deterministic test doubles.
/// </remarks>
public abstract class IntegrationTestBase : IDisposable
{
    private readonly string _dbPath;
    private readonly ITemporalResolver _temporalResolver;
    private bool _disposed;

    /// <summary>Options bound to this test's temp database.</summary>
    protected DbContextOptions<EasySynQDbContext> Options { get; }

    /// <summary>Construct a fresh database for this test.</summary>
    protected IntegrationTestBase()
    {
        (_dbPath, Options) = TempSqliteDb.Create();
        _temporalResolver = new CurrentTimeTemporalResolver(new SystemClock());
    }

    /// <summary>Creates a new DbContext bound to this test's database.</summary>
    protected EasySynQDbContext NewContext() => new(Options, _temporalResolver);

    /// <summary>
    /// The current test's cancellation token. xUnit v3's <c>xUnit1051</c>
    /// analyzer requires async methods that accept a CancellationToken to
    /// receive one so tests are cancellable.
    /// </summary>
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
            TempSqliteDb.Delete(_dbPath);
        }

        _disposed = true;
    }
}
