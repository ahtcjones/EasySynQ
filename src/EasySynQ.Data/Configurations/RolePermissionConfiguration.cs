using EasySynQ.Domain.Entities.Identity;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasySynQ.Data.Configurations;

internal sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.HasKey(rp => rp.Id);
        builder.Property(rp => rp.Id).ValueGeneratedNever();

        builder.Property(rp => rp.RoleId).IsRequired();
        builder.Property(rp => rp.PermissionId).IsRequired();

        // Indexes for the two most-common query shapes:
        //   "what permissions does role X have right now" — by RoleId
        //   "who has permission Y" — by PermissionId
        // The composite (RoleId, PermissionId) index supports the
        // resolution query's join shape (UserRole → RolePermission keyed
        // on RoleId, looking up Permission rows by PermissionId).
        builder.HasIndex(rp => new { rp.RoleId, rp.PermissionId });
        builder.HasIndex(rp => rp.PermissionId);

        // Foreign keys to Role and Permission. Per ADR 0004, FKs are
        // permitted within tightly-coupled Identity entities. Mirrors
        // UserRoleConfiguration: OnDelete.Restrict means hard-deleting a
        // Role or Permission is blocked while RolePermissions reference
        // it. No inverse navigations on Role or Permission — the
        // relationship is owned by the RolePermission side.
        builder.HasOne(rp => rp.Role)
            .WithMany()
            .HasForeignKey(rp => rp.RoleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(rp => rp.Permission)
            .WithMany()
            .HasForeignKey(rp => rp.PermissionId)
            .OnDelete(DeleteBehavior.Restrict);

        // EffectivePeriod is a value object — flattened to two columns on
        // the owner's row. Mirrors UserRoleConfiguration.
        builder.OwnsOne(rp => rp.EffectivePeriod, period =>
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
