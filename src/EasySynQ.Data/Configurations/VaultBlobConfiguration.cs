using EasySynQ.Domain.Entities.Documents;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasySynQ.Data.Configurations;

internal sealed class VaultBlobConfiguration : IEntityTypeConfiguration<VaultBlob>
{
    public void Configure(EntityTypeBuilder<VaultBlob> builder)
    {
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).ValueGeneratedNever();

        // SHA-256 hash is exactly 64 lowercase hex chars (HashFormat).
        // Unique because content addressing requires hash uniqueness —
        // this is the dedup mechanism per SPEC §3.6 + ADR 0008.
        builder.Property(b => b.Sha256Hash).IsRequired().HasMaxLength(64).IsFixedLength();
        builder.HasIndex(b => b.Sha256Hash).IsUnique();

        builder.Property(b => b.FileSizeBytes).IsRequired();
        builder.Property(b => b.MimeType).IsRequired().HasMaxLength(128);
        builder.Property(b => b.OriginalFileName).IsRequired().HasMaxLength(256);
        builder.Property(b => b.StoredAtUtc).IsRequired();

        builder.ConfigureAuditableEntityFields();
    }
}
