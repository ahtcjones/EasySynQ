using EasySynQ.Domain.Entities.Documents;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasySynQ.Data.Configurations;

internal sealed class DocumentLinkConfiguration : IEntityTypeConfiguration<DocumentLink>
{
    public void Configure(EntityTypeBuilder<DocumentLink> builder)
    {
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();

        builder.Property(l => l.DocumentRevisionId).IsRequired();
        builder.Property(l => l.ExternalDocumentId).IsRequired();
        builder.Property(l => l.CompatibilityReviewRequiredFlag).IsRequired();

        // Hard FKs within the Document aggregate per ADR 0004.
        builder.HasOne<DocumentRevision>()
            .WithMany()
            .HasForeignKey(l => l.DocumentRevisionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ExternalDocument>()
            .WithMany()
            .HasForeignKey(l => l.ExternalDocumentId)
            .OnDelete(DeleteBehavior.Restrict);

        // Indexes per ADR 0008 Implementation Notes: ExternalDocumentId
        // supports "all internal docs linked to this external" (drives
        // the compatibility-flag cascade when an external is updated).
        // DocumentRevisionId supports "all external links for this
        // revision".
        builder.HasIndex(l => l.DocumentRevisionId);
        builder.HasIndex(l => l.ExternalDocumentId);

        builder.ConfigureAuditableEntityFields();
    }
}
