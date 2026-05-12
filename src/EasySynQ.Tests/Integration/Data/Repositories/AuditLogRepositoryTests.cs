using AwesomeAssertions;

using EasySynQ.Data.Repositories;
using EasySynQ.Domain.Entities.Audit;
using EasySynQ.Domain.Entities.Identity;
using EasySynQ.Domain.Enums;
using EasySynQ.Tests.Integration.Data.Interceptors;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace EasySynQ.Tests.Integration.Data.Repositories;

public class AuditLogRepositoryTests : InterceptorIntegrationTestBase
{
    [Fact]
    public async Task ByEntity_ReturnsOnlyMatchingEntriesAsync()
    {
        // Insert two roles (each producing one audit entry).
        var targetRole = new Role(Guid.NewGuid(), "Target", "Will be queried.");
        var otherRole = new Role(Guid.NewGuid(), "Other", "Will not.");
        await using (var ctx = NewContext())
        {
            ctx.Roles.Add(targetRole);
            ctx.Roles.Add(otherRole);
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            var repo = new AuditLogRepository(ctx);
            var entries = await repo.ByEntity("Role", targetRole.Id.ToString()).ToListAsync(Ct);
            entries.Should().ContainSingle();
            entries[0].EntityId.Should().Be(targetRole.Id.ToString());
            entries[0].Action.Should().Be(AuditAction.Insert);
        }
    }

    [Fact]
    public async Task ByCorrelation_ReturnsAllEntriesWithThatIdAsync()
    {
        var correlationId = Guid.NewGuid();
        Correlation.CurrentCorrelationId = correlationId;

        var users = Enumerable.Range(0, 3)
            .Select(i => new User(Guid.NewGuid(), $"u{i}", $"U{i}", "h", "s", 600_000, false))
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
            var repo = new AuditLogRepository(ctx);
            var entries = await repo.ByCorrelation(correlationId).ToListAsync(Ct);
            entries.Should().HaveCount(3);
            entries.Should().AllSatisfy(e => e.CorrelationId.Should().Be(correlationId));
        }
    }

    [Fact]
    public async Task InTimeRange_FiltersInclusiveFromExclusiveToAsync()
    {
        // Three inserts at three different clock instants → three audit
        // entries with different UtcTimestamp.
        Clock.UtcNow = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var earlyRole = new Role(Guid.NewGuid(), "Early", "Jan 1.");
        await using (var ctx = NewContext())
        {
            ctx.Roles.Add(earlyRole);
            await ctx.SaveChangesAsync(Ct);
        }

        Clock.UtcNow = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var midRole = new Role(Guid.NewGuid(), "Mid", "Jun 1.");
        await using (var ctx = NewContext())
        {
            ctx.Roles.Add(midRole);
            await ctx.SaveChangesAsync(Ct);
        }

        Clock.UtcNow = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc);
        var lateRole = new Role(Guid.NewGuid(), "Late", "Dec 31.");
        await using (var ctx = NewContext())
        {
            ctx.Roles.Add(lateRole);
            await ctx.SaveChangesAsync(Ct);
        }

        // Range [Jan 1, Dec 31): early included (inclusive on from),
        // mid included, late excluded (exclusive on to).
        await using (var queryCtx = NewContext())
        {
            var repo = new AuditLogRepository(queryCtx);
            var inRange = await repo.InTimeRange(
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc))
                .ToListAsync(Ct);

            inRange.Should().HaveCount(2);
            inRange.Should().Contain(a => a.EntityId == earlyRole.Id.ToString());
            inRange.Should().Contain(a => a.EntityId == midRole.Id.ToString());
            inRange.Should().NotContain(a => a.EntityId == lateRole.Id.ToString());
        }
    }

    [Fact]
    public void SoftDelete_ThrowsInvalidOperationException()
    {
        using var ctx = NewContext();
        var repo = new AuditLogRepository(ctx);
        var entry = SampleEntry();

        Action act = () => repo.SoftDelete(entry);
        act.Should().Throw<InvalidOperationException>().WithMessage("*append-only*");
    }

    [Fact]
    public void HardDelete_ThrowsInvalidOperationException()
    {
        using var ctx = NewContext();
        var repo = new AuditLogRepository(ctx);
        var entry = SampleEntry();

        Action act = () => repo.HardDelete(entry);
        act.Should().Throw<InvalidOperationException>().WithMessage("*append-only*");
    }

    [Fact]
    public void InTimeRange_RejectsNonUtcFrom()
    {
        using var ctx = NewContext();
        var repo = new AuditLogRepository(ctx);
        var local = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Local);

        Action act = () => repo.InTimeRange(local, DateTime.UtcNow);
        act.Should().Throw<ArgumentException>().WithMessage("*DateTimeKind.Utc*");
    }

    private static AuditLogEntry SampleEntry() => new(
        Guid.NewGuid(),
        new DateTime(2026, 5, 12, 12, 0, 0, DateTimeKind.Utc),
        userId: null,
        "User", "u-1", AuditAction.Insert,
        before: null, after: "{}",
        correlationId: Guid.NewGuid());
}
