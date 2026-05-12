using EasySynQ.Domain.Entities.Audit;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasySynQ.Data.Configurations;

internal sealed class AuditLogEntryConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    public void Configure(EntityTypeBuilder<AuditLogEntry> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.UtcTimestamp).IsRequired();
        builder.Property(a => a.UserId); // nullable for system entries

        builder.Property(a => a.EntityTypeName).IsRequired().HasMaxLength(256);
        builder.Property(a => a.EntityId).IsRequired().HasMaxLength(64);

        // Store the AuditAction enum as a string for human-readable rows in
        // ad-hoc SQLite inspection.
        builder.Property(a => a.Action)
            .HasConversion<string>()
            .IsRequired()
            .HasMaxLength(16);

        // JSON snapshots can be arbitrarily large — no length cap.
        builder.Property(a => a.Before);
        builder.Property(a => a.After);

        builder.Property(a => a.CorrelationId).IsRequired();

        // Audit-log lookup indexes:
        //   "everything that happened to entity X" — (EntityTypeName, EntityId, UtcTimestamp)
        //   "everything in user-facing operation Y" — (CorrelationId)
        builder.HasIndex(a => new { a.EntityTypeName, a.EntityId, a.UtcTimestamp });
        builder.HasIndex(a => a.CorrelationId);

        // No soft-delete filter — AuditLogEntry does not inherit AuditableEntity
        // and has no IsDeleted column (SPEC §3.4: the table itself is the trail).
    }
}
