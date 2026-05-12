using AwesomeAssertions;

using EasySynQ.Domain.Entities.Identity;
using EasySynQ.Domain.Enums;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace EasySynQ.Tests.Integration.Data.Interceptors;

public class AuditLogWriteTests : InterceptorIntegrationTestBase
{
    [Fact]
    public async Task Insert_WritesAuditEntry_BeforeIsNullAsync()
    {
        var user = new User(Guid.NewGuid(), "alice", "Alice", "h", "s", 600_000, false);
        await using (var ctx = NewContext())
        {
            ctx.Users.Add(user);
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            var entry = await ctx.AuditLogEntries
                .SingleAsync(a => a.EntityTypeName == "User" && a.EntityId == user.Id.ToString(), Ct);

            entry.Action.Should().Be(AuditAction.Insert);
            entry.Before.Should().BeNull();
            entry.After.Should().NotBeNullOrEmpty();
        }
    }

    [Fact]
    public async Task Update_WritesAuditEntry_BothBeforeAndAfterPopulatedAsync()
    {
        var user = new User(Guid.NewGuid(), "alice", "Alice", "h", "s", 600_000, false);
        await using (var ctx = NewContext())
        {
            ctx.Users.Add(user);
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            var loaded = await ctx.Users.SingleAsync(u => u.Id == user.Id, Ct);
            ctx.Entry(loaded).Property(nameof(User.MustChangePassword)).CurrentValue = true;
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            var entries = await ctx.AuditLogEntries
                .Where(a => a.EntityTypeName == "User" && a.EntityId == user.Id.ToString())
                .OrderBy(a => a.UtcTimestamp)
                .ThenBy(a => a.Action)
                .ToListAsync(Ct);

            entries.Should().HaveCount(2);
            var updateEntry = entries.Single(e => e.Action == AuditAction.Update);
            updateEntry.Before.Should().NotBeNullOrEmpty();
            updateEntry.After.Should().NotBeNullOrEmpty();
        }
    }

    [Fact]
    public async Task SoftDelete_WritesAuditEntry_ActionIsDeleteAsync()
    {
        var user = new User(Guid.NewGuid(), "alice", "Alice", "h", "s", 600_000, false);
        await using (var ctx = NewContext())
        {
            ctx.Users.Add(user);
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            var loaded = await ctx.Users.SingleAsync(u => u.Id == user.Id, Ct);
            ctx.Entry(loaded).Property(nameof(User.IsDeleted)).CurrentValue = true;
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            var deleteEntry = await ctx.AuditLogEntries
                .SingleAsync(a => a.Action == AuditAction.Delete && a.EntityId == user.Id.ToString(), Ct);

            deleteEntry.EntityTypeName.Should().Be("User");
            deleteEntry.Before.Should().NotBeNullOrEmpty();
            deleteEntry.After.Should().NotBeNullOrEmpty();
        }
    }

    [Fact]
    public async Task HardDelete_WritesAuditEntry_AfterIsNullAsync()
    {
        // Hard-delete signaling: EntityState.Deleted from the change tracker.
        // The interceptor sees state=Deleted and emits action=HardDelete with
        // After=null per ADR 0002.
        var role = new Role(Guid.NewGuid(), "DraftRole", "Drafty draft.");
        await using (var ctx = NewContext())
        {
            ctx.Roles.Add(role);
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            var loaded = await ctx.Roles.SingleAsync(r => r.Id == role.Id, Ct);
            ctx.Roles.Remove(loaded);
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            var hardDeleteEntry = await ctx.AuditLogEntries
                .SingleAsync(a => a.Action == AuditAction.HardDelete && a.EntityId == role.Id.ToString(), Ct);

            hardDeleteEntry.Before.Should().NotBeNullOrEmpty();
            hardDeleteEntry.After.Should().BeNull();
        }
    }

    [Fact]
    public async Task ThreeEntitiesInOneSave_ProduceThreeAuditEntries_SameCorrelationIdAsync()
    {
        var user = new User(Guid.NewGuid(), "alice", "Alice", "h", "s", 600_000, false);
        var roleA = new Role(Guid.NewGuid(), "QM", "Quality Manager.");
        var roleB = new Role(Guid.NewGuid(), "LT", "Lab Technician.");

        await using (var ctx = NewContext())
        {
            ctx.Users.Add(user);
            ctx.Roles.Add(roleA);
            ctx.Roles.Add(roleB);
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            var entries = await ctx.AuditLogEntries.ToListAsync(Ct);
            entries.Should().HaveCount(3);
            entries.Select(e => e.CorrelationId).Distinct().Should().ContainSingle();
        }
    }

    [Fact]
    public async Task AuditLogEntryItself_DoesNotProduceFurtherAuditEntriesAsync()
    {
        // Exactly one entry written per User insert — not 1 + 1 (no
        // audit-of-audit). The loop-prevention guard in the interceptor
        // skips AuditLogEntry tracked entries explicitly.
        var user = new User(Guid.NewGuid(), "alice", "Alice", "h", "s", 600_000, false);
        await using (var ctx = NewContext())
        {
            ctx.Users.Add(user);
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            var totalEntries = await ctx.AuditLogEntries.CountAsync(Ct);
            totalEntries.Should().Be(1);
        }
    }
}
