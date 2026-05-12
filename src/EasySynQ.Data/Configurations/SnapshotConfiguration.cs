using EasySynQ.Domain.Entities.Snapshots;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasySynQ.Data.Configurations;

internal sealed class SnapshotConfiguration : IEntityTypeConfiguration<Snapshot>
{
    public void Configure(EntityTypeBuilder<Snapshot> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.Tier)
            .HasConversion<string>()
            .IsRequired()
            .HasMaxLength(16);

        builder.Property(s => s.FilePath).IsRequired().HasMaxLength(512);

        // SHA-256 hash is exactly 64 lowercase hex chars (HashFormat).
        builder.Property(s => s.ZipSha256).IsRequired().HasMaxLength(64).IsFixedLength();

        builder.Property(s => s.ByteSize).IsRequired();
        builder.Property(s => s.IncludedDatabaseSize).IsRequired();
        builder.Property(s => s.IncludedVaultSize).IsRequired();
        builder.Property(s => s.IntegrityVerified).IsRequired();
        builder.Property(s => s.IntegrityVerifiedUtc);

        // Lookup index: "newest Daily snapshots", "retain weeklies older than X".
        // CreatedUtc is inherited from AuditableEntity — same field, this is
        // the canonical timestamp for retention/age queries.
        builder.HasIndex(s => new { s.Tier, s.CreatedUtc });

        builder.ConfigureAuditableEntityFields();
    }
}
