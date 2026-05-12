using EasySynQ.Domain.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasySynQ.Data.Configurations;

/// <summary>
/// Shared EF Core configuration for the inherited fields on
/// <see cref="AuditableEntity"/>. Applied once per entity type from its
/// individual <c>IEntityTypeConfiguration</c> implementation so the
/// audit/standard-field mapping stays DRY.
/// </summary>
internal static class AuditableEntityConfigurationExtensions
{
    /// <summary>
    /// Configures the standard audit fields (<c>CreatedBy</c>, <c>CreatedUtc</c>,
    /// <c>ModifiedBy</c>, <c>ModifiedUtc</c>, <c>RowVersion</c>,
    /// <c>IsDeleted</c>) and applies the soft-delete global query filter
    /// excluding <c>IsDeleted = true</c> rows by default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The soft-delete filter is the *only* global query filter applied here.
    /// The effective-dating "as-of" filter is a separate concern wired in
    /// Chunk B's <c>TemporalResolver</c> registration; it is not represented
    /// at the entity-configuration level because it is scope-bound rather
    /// than property-bound.
    /// </para>
    /// <para>
    /// <c>RowVersion</c> is mapped as <see cref="IsConcurrencyToken"/> rather
    /// than the conventional <c>IsRowVersion</c>. The two differ in one
    /// important behavior: <c>IsRowVersion</c> implies
    /// <c>ValueGeneratedOnAddOrUpdate</c>, which assumes the DB auto-generates
    /// the token. SQLite has no such mechanism, so EF excludes the column
    /// from INSERT statements — and the NOT NULL byte[] column rejects the
    /// resulting NULL. <see cref="IsConcurrencyToken"/> keeps the WHERE-clause
    /// optimistic-concurrency check on UPDATE while letting EF include the
    /// column in INSERT and SET clauses, which is what we want: the Chunk B
    /// interceptor will assign a new <c>RowVersion</c> before save, and EF
    /// emits that value as a normal column update.
    /// </para>
    /// </remarks>
    public static EntityTypeBuilder<TEntity> ConfigureAuditableEntityFields<TEntity>(
        this EntityTypeBuilder<TEntity> builder)
        where TEntity : AuditableEntity
    {
        builder.Property(e => e.CreatedBy).IsRequired().HasMaxLength(64);
        builder.Property(e => e.CreatedUtc).IsRequired();
        builder.Property(e => e.ModifiedBy).IsRequired().HasMaxLength(64);
        builder.Property(e => e.ModifiedUtc).IsRequired();
        builder.Property(e => e.RowVersion).IsConcurrencyToken();
        builder.Property(e => e.IsDeleted).IsRequired();

        builder.HasQueryFilter(e => !e.IsDeleted);

        return builder;
    }
}
