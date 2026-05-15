using EasySynQ.Domain.Entities.Documents;
using EasySynQ.Domain.Enums;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasySynQ.Data.Configurations;

internal sealed class DocumentRevisionConfiguration : IEntityTypeConfiguration<DocumentRevision>
{
    public void Configure(EntityTypeBuilder<DocumentRevision> builder)
    {
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.DocumentId).IsRequired();
        builder.Property(r => r.RevisionLabel).IsRequired().HasMaxLength(64);

        // Lifecycle stored as TEXT per ADR 0008 — readability in raw
        // queries outweighs the 2× storage cost vs. the integer
        // backing.
        builder.Property(r => r.Lifecycle)
            .IsRequired()
            .HasMaxLength(16)
            .HasConversion<string>();

        builder.Property(r => r.EffectiveFromUtc);
        builder.Property(r => r.ApprovedAtUtc);

        builder.Property(r => r.VaultBlobId);
        builder.Property(r => r.AuthorUserId).IsRequired();
        builder.Property(r => r.AuthorSignatureId);

        // LockedAtUtc from SignableEntity. Configured inline because
        // DocumentRevision is C1's only SignableEntity-derived entity;
        // when more SignableEntities arrive (Job, NCR, etc.), promote
        // this to a ConfigureSignableEntityFields() helper alongside
        // ConfigureAuditableEntityFields.
        builder.Property("LockedAtUtc");

        // Hard FK to parent Document — within-aggregate per ADR 0004.
        builder.HasOne<Document>()
            .WithMany()
            .HasForeignKey(r => r.DocumentId)
            .OnDelete(DeleteBehavior.Restrict);

        // Hard FK to VaultBlob (nullable) — within-aggregate. The
        // nullable FK column means revisions can exist before a file
        // is attached, supporting the "draft text + upload later"
        // workflow.
        builder.HasOne<VaultBlob>()
            .WithMany()
            .HasForeignKey(r => r.VaultBlobId)
            .OnDelete(DeleteBehavior.Restrict);

        // Indexes per ADR 0008 Implementation Notes:
        //   DocumentId — "all revisions of doc X"
        //   Lifecycle  — "all Active revisions" / "all Approved revisions"
        //   EffectiveFromUtc — as-of queries
        builder.HasIndex(r => r.DocumentId);
        builder.HasIndex(r => r.Lifecycle);
        builder.HasIndex(r => r.EffectiveFromUtc);

        builder.ConfigureAuditableEntityFields();
    }
}
