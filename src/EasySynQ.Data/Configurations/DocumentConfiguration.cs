using EasySynQ.Domain.Entities.Documents;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasySynQ.Data.Configurations;

internal sealed class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();

        builder.Property(d => d.Number).IsRequired().HasMaxLength(64);
        builder.Property(d => d.Title).IsRequired().HasMaxLength(256);

        // Number is org-assigned and unique within the deployment per
        // SPEC §5.1 + ADR 0008.
        builder.HasIndex(d => d.Number).IsUnique();

        // Soft-reference columns to cross-aggregate entities (User, Signature)
        // per ADR 0004 — declared as nullable scalar Guids, no FK.
        builder.Property(d => d.RetiredAtUtc);
        builder.Property(d => d.RetiredByUserId);
        builder.Property(d => d.RetirementSignatureId);

        builder.ConfigureAuditableEntityFields();
    }
}
