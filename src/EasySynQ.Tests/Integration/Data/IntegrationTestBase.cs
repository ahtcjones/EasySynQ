using EasySynQ.Data.Context;
using EasySynQ.Tests.TestHelpers;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace EasySynQ.Tests.Integration.Data;

/// <summary>
/// Base class for Data-layer integration tests. Creates a fresh temp SQLite
/// database per test (xUnit instantiates a new test class per test method),
/// applies migrations, and cleans up on Dispose.
/// </summary>
public abstract class IntegrationTestBase : IDisposable
{
    private readonly string _dbPath;
    private bool _disposed;

    /// <summary>Options bound to this test's temp database.</summary>
    protected DbContextOptions<EasySynQDbContext> Options { get; }

    /// <summary>Construct a fresh database for this test.</summary>
    protected IntegrationTestBase()
    {
        (_dbPath, Options) = TempSqliteDb.Create();
    }

    /// <summary>Creates a new DbContext bound to this test's database.</summary>
    protected EasySynQDbContext NewContext() => new(Options);

    /// <summary>
    /// The current test's cancellation token. xUnit v3's <c>xUnit1051</c>
    /// analyzer requires async methods that accept a CancellationToken to
    /// receive one so tests are cancellable; passing
    /// <see cref="TestContext.Current"/>'s token threads cancellation
    /// through every EF Core await in the integration suite.
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
