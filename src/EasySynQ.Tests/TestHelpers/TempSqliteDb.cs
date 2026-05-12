using EasySynQ.Data.Context;
using EasySynQ.Data.Interceptors;
using EasySynQ.Services.Abstractions;
using EasySynQ.Services.Audit;
using EasySynQ.Services.Time;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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
    /// Releases connection-pool handles and deletes the temp file. Safe
    /// to call even if the file is gone.
    /// </summary>
    public static void Delete(string path)
    {
        SqliteConnection.ClearAllPools();

        try
        {
            if (System.IO.File.Exists(path))
            {
                System.IO.File.Delete(path);
            }
        }
        catch (System.IO.IOException)
        {
            // Best-effort cleanup; SQLite occasionally holds the file
            // briefly after pool clear on Windows. The next test gets a
            // unique path so a stale file here doesn't break isolation.
        }
    }

    private static string NewTempPath() => System.IO.Path.Combine(
        System.IO.Path.GetTempPath(),
        $"easysynq-{Guid.NewGuid():N}.db");
}
