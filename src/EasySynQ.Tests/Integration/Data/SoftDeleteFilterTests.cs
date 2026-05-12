using AwesomeAssertions;

using EasySynQ.Domain.Entities.Audit;
using EasySynQ.Domain.Entities.Identity;
using EasySynQ.Domain.Enums;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace EasySynQ.Tests.Integration.Data;

public class SoftDeleteFilterTests : IntegrationTestBase
{
    [Fact]
    public async Task SoftDeletedUser_IsExcludedFromDefaultQueriesAsync()
    {
        var id = Guid.NewGuid();
        var user = new User(id, "alice", "Alice", "hashed", "salted", 600_000, mustChangePassword: false);

        await using (var ctx = NewContext())
        {
            ctx.Users.Add(user);
            await ctx.SaveChangesAsync(Ct);

            // Flip IsDeleted via the Entry API since the property has a
            // protected setter (the standard pattern for "field that the
            // persistence layer manages but the domain wants encapsulated").
            ctx.Entry(user).Property(nameof(User.IsDeleted)).CurrentValue = true;
            await ctx.SaveChangesAsync(Ct);
        }

        await using var queryCtx = NewContext();

        // Default query path: soft-deleted row hidden.
        var defaultResult = await queryCtx.Users.SingleOrDefaultAsync(u => u.Id == id, Ct);
        defaultResult.Should().BeNull();

        // IgnoreQueryFilters opts out of the soft-delete filter explicitly.
        var ignoredResult = await queryCtx.Users
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(u => u.Id == id, Ct);
        ignoredResult.Should().NotBeNull();
        ignoredResult!.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task AuditLogEntry_HasNoSoftDeleteFilterAsync()
    {
        // Sanity check: AuditLogEntry does NOT inherit AuditableEntity, has no
        // IsDeleted column, and no global query filter. The append-only
        // invariant (SPEC §3.4) means rows are never hidden from queries.
        var id = Guid.NewGuid();
        var entry = new AuditLogEntry(
            id, new DateTime(2026, 5, 11, 12, 0, 0, DateTimeKind.Utc),
            userId: null, "User", "user-1", AuditAction.Insert,
            before: null, after: "{}", correlationId: Guid.NewGuid());

        await using (var ctx = NewContext())
        {
            ctx.AuditLogEntries.Add(entry);
            await ctx.SaveChangesAsync(Ct);
        }

        await using var queryCtx = NewContext();
        // Should be visible to the default query — no filter to bypass.
        var loaded = await queryCtx.AuditLogEntries.SingleOrDefaultAsync(a => a.Id == id, Ct);
        loaded.Should().NotBeNull();
    }
}
