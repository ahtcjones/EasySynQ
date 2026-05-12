using EasySynQ.Domain.Entities.Identity;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasySynQ.Data.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).ValueGeneratedNever();

        builder.Property(u => u.Username).IsRequired().HasMaxLength(64);
        builder.HasIndex(u => u.Username).IsUnique();

        builder.Property(u => u.DisplayName).IsRequired().HasMaxLength(128);

        // PBKDF2 hash + salt are stored as opaque strings (encoding pinned by ADR 0004).
        // Hash length is generous (256) to allow any base64-encoded output up to 192 bytes.
        builder.Property(u => u.PasswordHash).IsRequired().HasMaxLength(256);
        builder.Property(u => u.PasswordSalt).IsRequired().HasMaxLength(64);
        builder.Property(u => u.PasswordIterationCount).IsRequired();

        builder.Property(u => u.MustChangePassword).IsRequired();
        builder.Property(u => u.LastLoginUtc);
        builder.Property(u => u.IsDisabled).IsRequired();

        builder.ConfigureAuditableEntityFields();
    }
}
