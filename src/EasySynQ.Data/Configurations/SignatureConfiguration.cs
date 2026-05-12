using EasySynQ.Domain.Entities.Audit;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasySynQ.Data.Configurations;

internal sealed class SignatureConfiguration : IEntityTypeConfiguration<Signature>
{
    public void Configure(EntityTypeBuilder<Signature> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.UtcTimestamp).IsRequired();
        builder.Property(s => s.RoleAtTimeOfSign).IsRequired().HasMaxLength(64);

        builder.Property(s => s.SignedEntityType).IsRequired().HasMaxLength(256);
        builder.Property(s => s.SignedEntityId).IsRequired().HasMaxLength(64);

        // SHA-256 hash is exactly 64 lowercase hex chars (HashFormat).
        builder.Property(s => s.PayloadHash).IsRequired().HasMaxLength(64).IsFixedLength();

        // Lookup index: "all signatures on entity X".
        builder.HasIndex(s => new { s.SignedEntityType, s.SignedEntityId });

        builder.ConfigureAuditableEntityFields();
    }
}
