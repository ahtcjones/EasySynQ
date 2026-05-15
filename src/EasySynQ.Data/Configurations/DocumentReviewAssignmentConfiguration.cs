using EasySynQ.Domain.Entities.Documents;
using EasySynQ.Domain.Enums;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasySynQ.Data.Configurations;

internal sealed class DocumentReviewAssignmentConfiguration
    : IEntityTypeConfiguration<DocumentReviewAssignment>
{
    public void Configure(EntityTypeBuilder<DocumentReviewAssignment> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.DocumentRevisionId).IsRequired();
        builder.Property(a => a.ReviewerUserId).IsRequired();
        builder.Property(a => a.AssignedAtUtc).IsRequired();
        builder.Property(a => a.AssignedByUserId).IsRequired();

        // Status stored as TEXT — same readability rationale as
        // DocumentRevision.Lifecycle.
        builder.Property(a => a.Status)
            .IsRequired()
            .HasMaxLength(16)
            .HasConversion<string>();

        builder.Property(a => a.SignedAtUtc);
        builder.Property(a => a.SignatureId);

        // Hard FK to parent DocumentRevision — within-aggregate per ADR
        // 0004.
        builder.HasOne<DocumentRevision>()
            .WithMany()
            .HasForeignKey(a => a.DocumentRevisionId)
            .OnDelete(DeleteBehavior.Restrict);

        // Index per ADR 0008 Implementation Notes — "all reviewers of
        // this submission". The status filter ("which assignments are
        // still Pending") composes naturally on top.
        builder.HasIndex(a => a.DocumentRevisionId);

        builder.ConfigureAuditableEntityFields();
    }
}
