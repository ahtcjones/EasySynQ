using EasySynQ.Data.Conventions;
using EasySynQ.Domain.Entities.Audit;
using EasySynQ.Domain.Entities.Identity;
using EasySynQ.Domain.Entities.Snapshots;

using Microsoft.EntityFrameworkCore;

namespace EasySynQ.Data.Context;

/// <summary>
/// Root EF Core <see cref="DbContext"/> for EasySynQ. Phase 1 exposes only the
/// foundation entities — <see cref="User"/>, <see cref="Role"/>,
/// <see cref="UserRole"/>, <see cref="AuditLogEntry"/>,
/// <see cref="Signature"/>, <see cref="LockReason"/>, and
/// <see cref="Snapshot"/> — that the audit, signature, and snapshot services
/// operate on. Downstream phases add additional <see cref="DbSet{T}"/>s for
/// their entities.
/// </summary>
/// <remarks>
/// The UTC-kind invariant carried by every domain <see cref="DateTime"/> is
/// enforced via a global value-converter convention
/// (<see cref="UtcDateTimeConverter"/>) so SQLite's
/// <see cref="DateTimeKind.Unspecified"/> reads never reach the domain
/// constructors. SaveChanges interceptors — audit-log writer, standard-fields
/// populator, row-version increment — are wired in Chunk B; this Chunk A
/// context is plumbing only.
/// </remarks>
public class EasySynQDbContext : DbContext
{
    /// <summary>
    /// Constructs the context with the supplied options.
    /// </summary>
    /// <param name="options">Context options, typically configured by the
    /// host's DI registration or by tests' temp-SQLite setup.</param>
    public EasySynQDbContext(DbContextOptions<EasySynQDbContext> options) : base(options)
    {
    }

    /// <summary>Local user accounts.</summary>
    public DbSet<User> Users => Set<User>();

    /// <summary>Named role definitions.</summary>
    public DbSet<Role> Roles => Set<Role>();

    /// <summary>Effective-dated role assignments per user.</summary>
    public DbSet<UserRole> UserRoles => Set<UserRole>();

    /// <summary>Append-only audit-log entries (SPEC §3.4).</summary>
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();

    /// <summary>Digital-signature records (SPEC §3.4).</summary>
    public DbSet<Signature> Signatures => Set<Signature>();

    /// <summary>Lock-reason chains (SPEC §4.3).</summary>
    public DbSet<LockReason> LockReasons => Set<LockReason>();

    /// <summary>Snapshot manifests (SPEC §3.3).</summary>
    public DbSet<Snapshot> Snapshots => Set<Snapshot>();

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>()
            .HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>()
            .HaveConversion<UtcNullableDateTimeConverter>();

        base.ConfigureConventions(configurationBuilder);
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(EasySynQDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
