using EasySynQ.Domain.Entities.Audit;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasySynQ.Data.Configurations;

internal sealed class LockReasonConfiguration : IEntityTypeConfiguration<LockReason>
{
    public void Configure(EntityTypeBuilder<LockReason> builder)
    {
        builder.HasKey(lr => lr.Id);
        builder.Property(lr => lr.Id).ValueGeneratedNever();

        builder.Property(lr => lr.LockedEntityType).IsRequired().HasMaxLength(256);
        builder.Property(lr => lr.LockedEntityId).IsRequired().HasMaxLength(64);

        // Chain is an owned collection of LockReasonLink. Stored as JSON in a
        // single column on the LockReason row rather than as a separate table.
        //
        // Why JSON-in-column for THIS collection:
        //   1. A chain is meaningful only as a unit — links are read together
        //      and rebuilt together, never queried individually.
        //   2. SQLite supports JSON1 natively; serialization is cheap and
        //      avoids a relational JOIN for the lock-inspector hot path.
        //   3. The chain has no foreign-key relationships of its own — every
        //      navigational reference inside a link is a soft string pointer
        //      (LockReasonLink.NavigationEntityType/Id), not an FK.
        //   4. Schema migrations are cleaner — adding a field to LockReasonLink
        //      is a JSON-shape change, not an ALTER TABLE on a child table.
        //
        // LockReason.Chain is exposed as IReadOnlyList<LockReasonLink> over a
        // private List<LockReasonLink> backing field (_chain). EF Core reads
        // the field directly via PropertyAccessMode.Field and mutates the list
        // in place during materialization.
        builder.OwnsMany(lr => lr.Chain, chain =>
        {
            chain.ToJson();
            chain.Property(l => l.Tag).IsRequired().HasMaxLength(64);
            chain.Property(l => l.Id).IsRequired().HasMaxLength(128);
            chain.Property(l => l.Detail).IsRequired().HasMaxLength(1024);
            chain.Property(l => l.NavigationEntityType).HasMaxLength(256);
            chain.Property(l => l.NavigationEntityId).HasMaxLength(64);
            chain.Property(l => l.Because).HasMaxLength(512);
            chain.Property(l => l.IsTerminal).IsRequired();
        });

        builder.Navigation(lr => lr.Chain).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Lookup index: "is this entity locked?"
        builder.HasIndex(lr => new { lr.LockedEntityType, lr.LockedEntityId });

        builder.ConfigureAuditableEntityFields();
    }
}
