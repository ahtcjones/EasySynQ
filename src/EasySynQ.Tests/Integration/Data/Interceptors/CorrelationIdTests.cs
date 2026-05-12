using AwesomeAssertions;

using EasySynQ.Domain.Entities.Identity;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace EasySynQ.Tests.Integration.Data.Interceptors;

public class CorrelationIdTests : InterceptorIntegrationTestBase
{
    [Fact]
    public async Task EntitiesInOneSave_ShareCorrelationIdAsync()
    {
        var users = Enumerable.Range(0, 3)
            .Select(i => new User(Guid.NewGuid(), $"u{i}", $"User {i}", "h", "s", 600_000, false))
            .ToList();

        await using (var ctx = NewContext())
        {
            foreach (var u in users)
            {
                ctx.Users.Add(u);
            }
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
    public async Task DifferentSaves_GetDifferentDefaultCorrelationIdsAsync()
    {
        // Correlation.CurrentCorrelationId is null (default). Each save
        // generates a fresh correlation id.
        var userA = new User(Guid.NewGuid(), "a", "A", "h", "s", 600_000, false);
        var userB = new User(Guid.NewGuid(), "b", "B", "h", "s", 600_000, false);

        await using (var ctx = NewContext())
        {
            ctx.Users.Add(userA);
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            ctx.Users.Add(userB);
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            var entries = await ctx.AuditLogEntries
                .OrderBy(e => e.UtcTimestamp)
                .ToListAsync(Ct);
            entries.Should().HaveCount(2);
            entries[0].CorrelationId.Should().NotBe(entries[1].CorrelationId);
        }
    }

    [Fact]
    public async Task ExplicitCorrelationId_OverridesPerSaveDefaultAsync()
    {
        var explicitCorrelation = Guid.NewGuid();
        Correlation.CurrentCorrelationId = explicitCorrelation;

        var user = new User(Guid.NewGuid(), "alice", "Alice", "h", "s", 600_000, false);
        await using (var ctx = NewContext())
        {
            ctx.Users.Add(user);
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            var entry = await ctx.AuditLogEntries.SingleAsync(Ct);
            entry.CorrelationId.Should().Be(explicitCorrelation);
        }
    }

    [Fact]
    public async Task ExplicitCorrelationId_PersistsAcrossMultipleSavesAsync()
    {
        // If a UI command handler sets a correlation id and performs
        // multiple saves within that scope, all audit entries get the
        // same id — the logical "operation" is reconstructable.
        var operationCorrelation = Guid.NewGuid();
        Correlation.CurrentCorrelationId = operationCorrelation;

        var userA = new User(Guid.NewGuid(), "a", "A", "h", "s", 600_000, false);
        await using (var ctx = NewContext())
        {
            ctx.Users.Add(userA);
            await ctx.SaveChangesAsync(Ct);
        }

        var userB = new User(Guid.NewGuid(), "b", "B", "h", "s", 600_000, false);
        await using (var ctx = NewContext())
        {
            ctx.Users.Add(userB);
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            var entries = await ctx.AuditLogEntries.ToListAsync(Ct);
            entries.Should().HaveCount(2);
            entries.Should().AllSatisfy(e => e.CorrelationId.Should().Be(operationCorrelation));
        }
    }
}
