using EasySynQ.Data.Context;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EasySynQ.Tests.TestHelpers;

/// <summary>
/// Helper for integration tests that need a real SQLite-backed
/// <see cref="EasySynQDbContext"/>. Creates a unique temp file, applies all
/// EF Core migrations, and returns the bound options.
/// </summary>
/// <remarks>
/// ADR 0003 mandates that integration tests use a temp SQLite file (not the
/// in-memory <c>UseInMemoryDatabase</c> provider, which has subtly different
/// semantics for transactions and concurrency that have misled past projects).
/// </remarks>
public static class TempSqliteDb
{
    /// <summary>
    /// Creates a fresh temp SQLite database, runs migrations, and returns
    /// the file path plus options bound to it.
    /// </summary>
    public static (string Path, DbContextOptions<EasySynQDbContext> Options) Create()
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"easysynq-{Guid.NewGuid():N}.db");

        var options = new DbContextOptionsBuilder<EasySynQDbContext>()
            .UseSqlite($"Data Source={path}")
            .Options;

        using var ctx = new EasySynQDbContext(options);
        ctx.Database.Migrate();

        return (path, options);
    }

    /// <summary>
    /// Releases connection-pool handles and deletes the temp file. Safe to
    /// call even if the file is gone.
    /// </summary>
    public static void Delete(string path)
    {
        // Drop pooled connections so the file lock is released on Windows.
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
            // Best-effort cleanup; SQLite occasionally holds the file briefly
            // after pool clear on Windows. The next test gets a unique path
            // so a stale file here doesn't break isolation.
        }
    }
}
