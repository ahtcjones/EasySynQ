using EasySynQ.Domain.Entities.Identity;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasySynQ.Data.Configurations;

internal sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.Name).IsRequired().HasMaxLength(128);
        builder.HasIndex(p => p.Name).IsUnique();

        builder.Property(p => p.Description).IsRequired().HasMaxLength(512);
        builder.Property(p => p.Category).IsRequired().HasMaxLength(64);

        builder.ConfigureAuditableEntityFields();
    }
}
