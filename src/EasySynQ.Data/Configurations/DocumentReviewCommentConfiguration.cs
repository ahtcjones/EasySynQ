using EasySynQ.Domain.Entities.Documents;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasySynQ.Data.Configurations;

internal sealed class DocumentReviewCommentConfiguration
    : IEntityTypeConfiguration<DocumentReviewComment>
{
    public void Configure(EntityTypeBuilder<DocumentReviewComment> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.DocumentRevisionId).IsRequired();
        builder.Property(c => c.AuthorUserId).IsRequired();
        builder.Property(c => c.CreatedAtUtc).IsRequired();

        // BodyText is uncapped at the DB level per ADR 0008
        // Implementation Notes ("UI may enforce a soft cap for
        // sensible display"). EF Core's default for unconstrained
        // strings is TEXT on SQLite, which is what we want.
        builder.Property(c => c.BodyText).IsRequired();

        // Hard FK to parent DocumentRevision — within-aggregate per
        // ADR 0004.
        builder.HasOne<DocumentRevision>()
            .WithMany()
            .HasForeignKey(c => c.DocumentRevisionId)
            .OnDelete(DeleteBehavior.Restrict);

        // Index for the common query shape: "all comments on this
        // revision, in chronological order".
        builder.HasIndex(c => c.DocumentRevisionId);

        builder.ConfigureAuditableEntityFields();
    }
}
