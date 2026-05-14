using EasySynQ.Data.Conventions;
using EasySynQ.Domain.Entities.Audit;
using EasySynQ.Domain.Entities.Identity;
using EasySynQ.Domain.Entities.Snapshots;
using EasySynQ.Services.Abstractions;

using Microsoft.EntityFrameworkCore;

namespace EasySynQ.Data.Context;

/// <summary>
/// Root EF Core <see cref="DbContext"/> for EasySynQ. Phase 1 exposes the
/// foundation entities — <see cref="User"/>, <see cref="Role"/>,
/// <see cref="UserRole"/>, <see cref="AuditLogEntry"/>,
/// <see cref="Signature"/>, <see cref="LockReason"/>, and
/// <see cref="Snapshot"/> — on which the audit, signature, and snapshot
/// services operate. Later phases add their own entities; the DbContext
/// is the single root.
/// </summary>
/// <remarks>
/// <para>
/// <b>UTC kind enforcement.</b> Every <see cref="DateTime"/> read from
/// SQLite is converted to <see cref="DateTimeKind.Utc"/> via a global
/// value-converter convention (<see cref="UtcDateTimeConverter"/>) so the
/// domain's UTC-kind invariants survive round-trips.
/// </para>
/// <para>
/// <b>Effective-dating filter.</b> Every <see cref="IEffectiveDated"/>
/// entity gets a global query filter applied in
/// <see cref="OnModelCreating"/>. The filter references
/// <see cref="AsOfUtc"/> — a private property on this context — so EF
/// Core's expression visitor recognizes it as a <c>DbContext</c> member
/// access and parameterizes the value at every query execution. This is
/// the load-bearing detail that makes
/// <see cref="ITemporalResolver"/> live rather than snapshot-at-model-build:
/// historical-evaluation scopes that mutate the temporal resolver see
/// new results from the same compiled query without rebuilding the model.
/// </para>
/// <para>
/// An earlier attempt extracted the filter into a static helper class
/// (<c>EffectiveDatingQueryConfigurator</c>) that took an
/// <see cref="ITemporalResolver"/> as a parameter and referenced it in
/// the lambda. That pattern <b>does not work</b> in EF Core 10: the
/// closure-captured resolver flattens to a constant in the expression
/// tree and the filter snapshots the value at model-build time. Only
/// member access on the DbContext instance is parameterized live.
/// </para>
/// <para>
/// <b>Interceptors.</b> <c>StandardFieldsInterceptor</c> and
/// <c>AuditSaveChangesInterceptor</c> are wired via
/// <see cref="EasySynQDbContextOptionsExtensions.UseEasySynQInterceptors"/>
/// — they live on <see cref="DbContextOptions"/>, not as DbContext fields.
/// The context only needs <see cref="ITemporalResolver"/> directly (for
/// the query filter's parameterization).
/// </para>
/// </remarks>
public class EasySynQDbContext : DbContext
{
    private readonly ITemporalResolver _temporalResolver;

    /// <summary>
    /// Constructs the context with the supplied options and temporal
    /// resolver.
    /// </summary>
    /// <param name="options">EF Core options, typically configured by the
    /// host's DI registration (which also calls
    /// <see cref="EasySynQDbContextOptionsExtensions.UseEasySynQInterceptors"/>
    /// to add interceptors).</param>
    /// <param name="temporalResolver">Source of the "as of" instant for
    /// the effective-dating query filter. Must not be
    /// <see langword="null"/>; production hosts pass a
    /// <c>CurrentTimeTemporalResolver</c>, historical-evaluation scopes
    /// pass a fixed-instant resolver.</param>
    public EasySynQDbContext(
        DbContextOptions<EasySynQDbContext> options,
        ITemporalResolver temporalResolver)
        : base(options)
    {
        ArgumentNullException.ThrowIfNull(temporalResolver);
        _temporalResolver = temporalResolver;
    }

    /// <summary>Local user accounts.</summary>
    public DbSet<User> Users => Set<User>();

    /// <summary>Named role definitions.</summary>
    public DbSet<Role> Roles => Set<Role>();

    /// <summary>Authorization permission catalog (ADR 0007).</summary>
    public DbSet<Permission> Permissions => Set<Permission>();

    /// <summary>Effective-dated role assignments per user.</summary>
    public DbSet<UserRole> UserRoles => Set<UserRole>();

    /// <summary>Effective-dated permission grants per role (ADR 0007).</summary>
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    /// <summary>Effective-dated direct permission grants per user (ADR 0007).</summary>
    public DbSet<UserPermission> UserPermissions => Set<UserPermission>();

    /// <summary>Append-only audit-log entries (SPEC §3.4).</summary>
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();

    /// <summary>Digital-signature records (SPEC §3.4).</summary>
    public DbSet<Signature> Signatures => Set<Signature>();

    /// <summary>Lock-reason chains (SPEC §4.3).</summary>
    public DbSet<LockReason> LockReasons => Set<LockReason>();

    /// <summary>Snapshot manifests (SPEC §3.3).</summary>
    public DbSet<Snapshot> Snapshots => Set<Snapshot>();

    // Internal property used by the effective-dating query filter. The
    // filter references AsOfUtc via `this`, which EF Core's expression
    // visitor parameterizes at query time. Each query reads the current
    // value from the injected temporal resolver — historical scopes that
    // swap or mutate the resolver see live results without a model
    // rebuild.
    private DateTime AsOfUtc => _temporalResolver.AsOfUtc;

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

        // IEffectiveDated entity filters. Add new IEffectiveDated entities
        // here as they come online; the filter MUST reference AsOfUtc via
        // `this` (a member of this DbContext) — external closure
        // references over ITemporalResolver get snapshotted at
        // model-build time and never refresh.
        modelBuilder.Entity<UserRole>().HasQueryFilter(ur =>
            ur.EffectivePeriod.EffectiveFromUtc <= AsOfUtc
            && (ur.EffectivePeriod.EffectiveToUtc == null
                || AsOfUtc < ur.EffectivePeriod.EffectiveToUtc));

        modelBuilder.Entity<RolePermission>().HasQueryFilter(rp =>
            rp.EffectivePeriod.EffectiveFromUtc <= AsOfUtc
            && (rp.EffectivePeriod.EffectiveToUtc == null
                || AsOfUtc < rp.EffectivePeriod.EffectiveToUtc));

        modelBuilder.Entity<UserPermission>().HasQueryFilter(up =>
            up.EffectivePeriod.EffectiveFromUtc <= AsOfUtc
            && (up.EffectivePeriod.EffectiveToUtc == null
                || AsOfUtc < up.EffectivePeriod.EffectiveToUtc));

        base.OnModelCreating(modelBuilder);
    }
}
