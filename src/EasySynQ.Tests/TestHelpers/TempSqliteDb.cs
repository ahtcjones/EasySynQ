using EasySynQ.Data.Context;
using EasySynQ.Data.Interceptors;
using EasySynQ.Services.Abstractions;
using EasySynQ.Services.Audit;
using EasySynQ.Services.Time;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace EasySynQ.Tests.TestHelpers;

/// <summary>
/// Helper for integration tests that need a real SQLite-backed
/// <see cref="EasySynQDbContext"/>. Creates a unique temp file, applies
/// all EF Core migrations, and returns the bound options.
/// </summary>
/// <remarks>
/// ADR 0003 mandates that integration tests use a temp SQLite file (not
/// the in-memory <c>UseInMemoryDatabase</c> provider, which has subtly
/// different semantics for transactions and concurrency that have misled
/// past projects).
/// </remarks>
public static class TempSqliteDb
{
    /// <summary>
    /// Creates a fresh temp SQLite database, runs migrations, and returns
    /// the file path plus options bound to it. Bare options — no
    /// interceptors. For tests that exercise the interceptor pipeline,
    /// use <see cref="CreateWithInterceptors"/> instead.
    /// </summary>
    public static (string Path, DbContextOptions<EasySynQDbContext> Options) Create()
    {
        var path = NewTempPath();

        var options = new DbContextOptionsBuilder<EasySynQDbContext>()
            .UseSqlite($"Data Source={path}")
            .Options;

        var resolver = new CurrentTimeTemporalResolver(new SystemClock());
        using var ctx = new EasySynQDbContext(options, resolver);
        ctx.Database.Migrate();

        return (path, options);
    }

    /// <summary>
    /// Creates a fresh temp SQLite database and migrates it to a
    /// specific target migration (inclusive), leaving later migrations
    /// un-applied. Used by tests that need to set up a "pre-X state"
    /// data shape and then exercise migration X explicitly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The standard <see cref="Create"/> path applies every migration
    /// in the chain. This variant stops at
    /// <paramref name="targetMigrationName"/>; the caller can then
    /// seed test data into the partial schema and call
    /// <c>ctx.Database.Migrate()</c> on a freshly-opened context to
    /// apply the remaining migrations under the seeded preconditions.
    /// </para>
    /// <para>
    /// <see cref="IMigrator.Migrate(string)"/> accepts either the
    /// full timestamped migration name
    /// (<c>"20260514040551_AddPermissionsAndLinkTables"</c>) or the
    /// bare class name (<c>"AddPermissionsAndLinkTables"</c>); the
    /// bare name is friendlier to read in test code and survives
    /// timestamp drift if a migration is ever regenerated.
    /// </para>
    /// </remarks>
    public static (string Path, DbContextOptions<EasySynQDbContext> Options) CreateMigratedTo(
        string targetMigrationName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetMigrationName);

        var path = NewTempPath();

        var options = new DbContextOptionsBuilder<EasySynQDbContext>()
            .UseSqlite($"Data Source={path}")
            .Options;

        var resolver = new CurrentTimeTemporalResolver(new SystemClock());
        using var ctx = new EasySynQDbContext(options, resolver);
        var migrator = ctx.GetInfrastructure().GetRequiredService<IMigrator>();
        migrator.Migrate(targetMigrationName);

        return (path, options);
    }

    /// <summary>
    /// Creates a fresh temp SQLite database wired with both data-layer
    /// interceptors. Caller supplies the abstractions backing them so
    /// each test can use deterministic clocks, users, etc.
    /// </summary>
    public static (string Path, DbContextOptions<EasySynQDbContext> Options, IServiceProvider Services)
        CreateWithInterceptors(
            IClock clock,
            ICurrentUserAccessor currentUser,
            IAuditCorrelationProvider correlationProvider,
            ITemporalResolver temporalResolver)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(correlationProvider);
        ArgumentNullException.ThrowIfNull(temporalResolver);

        var path = NewTempPath();

        var services = new ServiceCollection();
        services.AddSingleton(clock);
        services.AddSingleton(currentUser);
        services.AddSingleton(correlationProvider);
        services.AddSingleton(temporalResolver);
        services.AddSingleton<StandardFieldsInterceptor>();
        services.AddSingleton<AuditSaveChangesInterceptor>();
        var sp = services.BuildServiceProvider();

        var options = new DbContextOptionsBuilder<EasySynQDbContext>()
            .UseSqlite($"Data Source={path}")
            .UseEasySynQInterceptors(sp)
            .Options;

        using (var ctx = new EasySynQDbContext(options, temporalResolver))
        {
            ctx.Database.Migrate();
        }

        return (path, options, sp);
    }

    /// <summary>
    /// Releases connection-pool handles for this database and deletes the
    /// temp file. Safe to call even if the file is gone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why flush the pool at all:</b> on Windows, SQLite occasionally
    /// holds a file lock briefly after a connection is returned to the
    /// pool. Without flushing, the subsequent <see cref="System.IO.File.Delete(string)"/>
    /// can race the lock release and throw <see cref="System.IO.IOException"/>
    /// ("the process cannot access the file...").
    /// </para>
    /// <para>
    /// <b>Why <c>ClearPool(connection)</c> and NOT <c>ClearAllPools()</c>:</b>
    /// <c>ClearAllPools()</c> is process-wide. Under xUnit v3's default
    /// parallel-test-collections execution, one test's teardown calling
    /// <c>ClearAllPools()</c> yanks pooled <see cref="SqliteConnection"/>
    /// instances out from under concurrently-running tests, producing
    /// <see cref="ObjectDisposedException"/> on the underlying
    /// <c>SQLitePCL.sqlite3</c> handle. The failing test in that scenario
    /// is always a bystander — the bug is in the teardown of whichever
    /// other test happened to dispose at the wrong moment. The targeted
    /// <see cref="SqliteConnection.ClearPool(SqliteConnection)"/> form
    /// scopes the flush to this connection string only, preserving the
    /// original Windows file-lock-release intent without disturbing any
    /// other test's pool.
    /// </para>
    /// <para>
    /// <b>Canonical regression check:</b> if you suspect this regressed,
    /// run the full test suite 30× at default parallelism. Anything
    /// meaningfully below a 100% pass rate, with failures across multiple
    /// integration-test classes all citing
    /// <c>ObjectDisposedException : Cannot access a disposed object.
    /// Object name: 'SQLitePCL.sqlite3'</c>, is this regression returning.
    /// </para>
    /// </remarks>
    public static void Delete(string path)
    {
        // The connection is never opened — ClearPool keys on the
        // connection string, so constructing the instance is sufficient
        // to identify which pool to flush.
        using (var conn = new SqliteConnection($"Data Source={path}"))
        {
            SqliteConnection.ClearPool(conn);
        }

        try
        {
            if (System.IO.File.Exists(path))
            {
                System.IO.File.Delete(path);
            }
        }
        catch (System.IO.IOException)
        {
            // Best-effort cleanup; if the file is somehow still locked
            // after the targeted pool flush, the next test gets a unique
            // GUID-named path so a stale file here doesn't break isolation.
        }
    }

    private static string NewTempPath() => System.IO.Path.Combine(
        System.IO.Path.GetTempPath(),
        $"easysynq-{Guid.NewGuid():N}.db");
}
