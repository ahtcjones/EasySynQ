using EasySynQ.Domain.Entities.Identity;
using EasySynQ.Domain.ValueObjects;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasySynQ.Data.Configurations;

internal sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        builder.HasKey(ur => ur.Id);
        builder.Property(ur => ur.Id).ValueGeneratedNever();

        builder.Property(ur => ur.UserId).IsRequired();
        builder.Property(ur => ur.RoleId).IsRequired();

        // Indexes for the two most-common query shapes:
        //   "what roles does user X hold" — by UserId
        //   "who holds role X right now"  — by RoleId
        builder.HasIndex(ur => ur.UserId);
        builder.HasIndex(ur => ur.RoleId);

        // Foreign keys to the User and Role aggregates. Per ADR 0004, FKs
        // are permitted within tightly-coupled Identity entities (User /
        // Role / UserRole co-evolve and ship together). OnDelete.Restrict
        // means hard-deleting a User or Role is blocked while UserRoles
        // reference it — orphans must be handled deliberately, not via
        // implicit cascade. Soft-deleting (flipping IsDeleted) does not
        // remove the row, so FKs continue to validate against table
        // presence regardless of the soft-delete state.
        //
        // No inverse navigations on User or Role — those aggregate roots
        // do not expose UserRoles collections (the relationship is owned
        // by the UserRole side; iterating roles-per-user is a service-
        // layer concern).
        builder.HasOne(ur => ur.User)
            .WithMany()
            .HasForeignKey(ur => ur.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(ur => ur.Role)
            .WithMany()
            .HasForeignKey(ur => ur.RoleId)
            .OnDelete(DeleteBehavior.Restrict);

        // EffectivePeriod is a value object — flattened to two columns on the
        // owner's row. EF Core's owned-type mapping handles materialization
        // through EffectiveDateRange's constructor.
        builder.OwnsOne(ur => ur.EffectivePeriod, period =>
        {
            period.Property(p => p.EffectiveFromUtc)
                .HasColumnName("EffectiveFromUtc")
                .IsRequired();

            period.Property(p => p.EffectiveToUtc)
                .HasColumnName("EffectiveToUtc");

            // Effective-dating "as-of" queries scan these two columns; the
            // index lets the query planner skip non-overlapping rows.
            period.HasIndex(p => p.EffectiveFromUtc);
            period.HasIndex(p => p.EffectiveToUtc);
        });

        builder.ConfigureAuditableEntityFields();
    }
}
