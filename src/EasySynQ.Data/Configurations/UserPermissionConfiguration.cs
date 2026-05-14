using EasySynQ.Domain.Entities.Identity;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasySynQ.Data.Configurations;

internal sealed class UserPermissionConfiguration : IEntityTypeConfiguration<UserPermission>
{
    public void Configure(EntityTypeBuilder<UserPermission> builder)
    {
        builder.HasKey(up => up.Id);
        builder.Property(up => up.Id).ValueGeneratedNever();

        builder.Property(up => up.UserId).IsRequired();
        builder.Property(up => up.PermissionId).IsRequired();

        // Indexes for the two most-common query shapes:
        //   "what direct permissions does user X have right now" — by UserId
        //   "who has direct permission Y" — by PermissionId
        builder.HasIndex(up => new { up.UserId, up.PermissionId });
        builder.HasIndex(up => up.PermissionId);

        // FKs per ADR 0004 — see RolePermissionConfiguration for the
        // shared rationale. No inverse navigations on User or Permission.
        builder.HasOne(up => up.User)
            .WithMany()
            .HasForeignKey(up => up.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(up => up.Permission)
            .WithMany()
            .HasForeignKey(up => up.PermissionId)
            .OnDelete(DeleteBehavior.Restrict);

        // EffectivePeriod owned-type mapping mirrors UserRoleConfiguration.
        builder.OwnsOne(up => up.EffectivePeriod, period =>
        {
            period.Property(p => p.EffectiveFromUtc)
                .HasColumnName("EffectiveFromUtc")
                .IsRequired();

            period.Property(p => p.EffectiveToUtc)
                .HasColumnName("EffectiveToUtc");

            period.HasIndex(p => p.EffectiveFromUtc);
            period.HasIndex(p => p.EffectiveToUtc);
        });

        builder.ConfigureAuditableEntityFields();
    }
}
