using AwesomeAssertions;

using EasySynQ.Domain.Entities.Identity;
using EasySynQ.Domain.ValueObjects;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace EasySynQ.Tests.Integration.Data.Interceptors;

public class EffectiveDatingFilterTests : InterceptorIntegrationTestBase
{
    [Fact]
    public async Task PastAsOf_ReturnsOnlyThenInEffectAssignmentAsync()
    {
        // Scenario: user holds QM role from 2024-01-01 to 2024-07-01, then
        // LT role from 2024-07-01 onward.
        var user = new User(Guid.NewGuid(), "alice", "Alice", "h", "s", 600_000, false);
        var qmRole = new Role(Guid.NewGuid(), "QM", "Quality Manager.");
        var ltRole = new Role(Guid.NewGuid(), "LT", "Lab Tech.");

        var qmAssignment = new UserRole(
            Guid.NewGuid(), user.Id, qmRole.Id,
            new EffectiveDateRange(
                new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2024, 7, 1, 0, 0, 0, DateTimeKind.Utc)));
        var ltAssignment = new UserRole(
            Guid.NewGuid(), user.Id, ltRole.Id,
            new EffectiveDateRange(
                new DateTime(2024, 7, 1, 0, 0, 0, DateTimeKind.Utc),
                null));

        await using (var ctx = NewContext())
        {
            ctx.Users.Add(user);
            ctx.Roles.Add(qmRole);
            ctx.Roles.Add(ltRole);
            ctx.UserRoles.Add(qmAssignment);
            ctx.UserRoles.Add(ltAssignment);
            await ctx.SaveChangesAsync(Ct);
        }

        // As of mid-March 2024 → only QM was in effect.
        TemporalResolver.AsOfUtc = new DateTime(2024, 3, 15, 0, 0, 0, DateTimeKind.Utc);
        await using (var ctx = NewContext())
        {
            var inEffect = await ctx.UserRoles.Where(u => u.UserId == user.Id).ToListAsync(Ct);
            inEffect.Should().ContainSingle();
            inEffect[0].Id.Should().Be(qmAssignment.Id);
        }
    }

    [Fact]
    public async Task FutureEffectiveFrom_IsExcludedByNowQueryAsync()
    {
        // Pre-scheduled role assignment (Rev 3.2 of the spec authorizes this):
        // starts a week from now. Query with AsOfUtc = now → not visible.
        var user = new User(Guid.NewGuid(), "bob", "Bob", "h", "s", 600_000, false);
        var role = new Role(Guid.NewGuid(), "QM", "Quality Manager.");
        var futureAssignment = new UserRole(
            Guid.NewGuid(), user.Id, role.Id,
            new EffectiveDateRange(
                Clock.UtcNow.AddDays(7),
                null));

        await using (var ctx = NewContext())
        {
            ctx.Users.Add(user);
            ctx.Roles.Add(role);
            ctx.UserRoles.Add(futureAssignment);
            await ctx.SaveChangesAsync(Ct);
        }

        TemporalResolver.AsOfUtc = Clock.UtcNow;
        await using (var ctx = NewContext())
        {
            var inEffect = await ctx.UserRoles.Where(u => u.UserId == user.Id).ToListAsync(Ct);
            inEffect.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task IgnoreQueryFilters_ReturnsAllRowsRegardlessOfDatingAsync()
    {
        var user = new User(Guid.NewGuid(), "carol", "Carol", "h", "s", 600_000, false);
        var role = new Role(Guid.NewGuid(), "QM", "Quality Manager.");
        var pastAssignment = new UserRole(
            Guid.NewGuid(), user.Id, role.Id,
            new EffectiveDateRange(
                new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2020, 12, 31, 0, 0, 0, DateTimeKind.Utc)));

        await using (var ctx = NewContext())
        {
            ctx.Users.Add(user);
            ctx.Roles.Add(role);
            ctx.UserRoles.Add(pastAssignment);
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            var filtered = await ctx.UserRoles.Where(u => u.UserId == user.Id).ToListAsync(Ct);
            filtered.Should().BeEmpty();

            var unfiltered = await ctx.UserRoles
                .IgnoreQueryFilters()
                .Where(u => u.UserId == user.Id)
                .ToListAsync(Ct);
            unfiltered.Should().ContainSingle();
        }
    }

    [Fact]
    public async Task ResolverMutation_IsObservedLiveAtQueryTime_NotSnapshottedAtModelBuildAsync()
    {
        // The point of this test: the temporal resolver is evaluated at
        // query time, not captured at OnModelCreating time. If the
        // resolver's AsOfUtc were snapshotted into the compiled filter
        // expression, mutating it here would have no effect on subsequent
        // queries. Instead, we observe live re-evaluation — the same
        // DbContext (and same options, and same cached model) returns
        // different results as the resolver's value changes.
        var user = new User(Guid.NewGuid(), "dave", "Dave", "h", "s", 600_000, false);
        var role = new Role(Guid.NewGuid(), "QM", "Quality Manager.");

        var spring2024 = new UserRole(
            Guid.NewGuid(), user.Id, role.Id,
            new EffectiveDateRange(
                new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc)));
        var fall2024 = new UserRole(
            Guid.NewGuid(), user.Id, role.Id,
            new EffectiveDateRange(
                new DateTime(2024, 9, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2024, 12, 1, 0, 0, 0, DateTimeKind.Utc)));

        await using (var ctx = NewContext())
        {
            ctx.Users.Add(user);
            ctx.Roles.Add(role);
            ctx.UserRoles.Add(spring2024);
            ctx.UserRoles.Add(fall2024);
            await ctx.SaveChangesAsync(Ct);
        }

        TemporalResolver.AsOfUtc = new DateTime(2024, 4, 15, 0, 0, 0, DateTimeKind.Utc);
        await using (var ctx = NewContext())
        {
            var spring = await ctx.UserRoles.Where(u => u.UserId == user.Id).ToListAsync(Ct);
            spring.Should().ContainSingle().Which.Id.Should().Be(spring2024.Id);
        }

        // Same options instance, cached model, mutated resolver.
        TemporalResolver.AsOfUtc = new DateTime(2024, 10, 15, 0, 0, 0, DateTimeKind.Utc);
        await using (var ctx = NewContext())
        {
            var fall = await ctx.UserRoles.Where(u => u.UserId == user.Id).ToListAsync(Ct);
            fall.Should().ContainSingle().Which.Id.Should().Be(fall2024.Id);
        }
    }
}
