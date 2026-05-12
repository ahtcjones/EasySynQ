using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EasySynQ.Data.Context;

/// <summary>
/// Design-time factory used by the EF Core CLI tooling
/// (<c>dotnet ef migrations add</c>, <c>dotnet ef database update</c>, etc.)
/// to construct an <see cref="EasySynQDbContext"/> without a running
/// application.
/// </summary>
/// <remarks>
/// EF Core's CLI normally locates its DbContext via a startup project's host
/// builder. <c>EasySynQ.Data</c> is a class library with no host and no
/// service provider, so the CLI falls back to this factory. The connection
/// string supplied here is only used at design time for tooling (migration
/// generation, model snapshot, etc.) and is never used at runtime — runtime
/// callers always supply their own <see cref="DbContextOptions"/>.
/// </remarks>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<EasySynQDbContext>
{
    /// <summary>
    /// Creates an <see cref="EasySynQDbContext"/> for design-time tooling.
    /// </summary>
    /// <param name="args">CLI arguments forwarded by the tool. Unused.</param>
    /// <returns>A context bound to a design-time-only SQLite connection.</returns>
    public EasySynQDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<EasySynQDbContext>();
        optionsBuilder.UseSqlite("Data Source=design-time-only.db");
        return new EasySynQDbContext(optionsBuilder.Options);
    }
}
