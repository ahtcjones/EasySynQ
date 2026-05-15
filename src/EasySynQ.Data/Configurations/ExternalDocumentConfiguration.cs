using EasySynQ.Domain.Entities.Documents;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasySynQ.Data.Configurations;

internal sealed class ExternalDocumentConfiguration : IEntityTypeConfiguration<ExternalDocument>
{
    public void Configure(EntityTypeBuilder<ExternalDocument> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.IssuingBody).IsRequired().HasMaxLength(64);
        builder.Property(e => e.Designation).IsRequired().HasMaxLength(128);
        builder.Property(e => e.CurrentRevisionLabel).IsRequired().HasMaxLength(64);
        builder.Property(e => e.CurrentEffectiveDateUtc);

        // Lookup index for the most-common query shape: "find the
        // external doc for ASTM A29" / "AMS 2750". The composite is
        // unique because the same issuing body should not have two
        // entries for the same designation (org policy enforced at the
        // schema level).
        builder.HasIndex(e => new { e.IssuingBody, e.Designation }).IsUnique();

        builder.ConfigureAuditableEntityFields();
    }
}
