using AwesomeAssertions;

using EasySynQ.Data.Repositories;
using EasySynQ.Domain.Entities.Identity;
using EasySynQ.Domain.ValueObjects;
using EasySynQ.Tests.Integration.Data;

using Xunit;

namespace EasySynQ.Tests.Integration.Data.Repositories;

/// <summary>
/// Exercises <see cref="UserRoleRepository.GetEffectiveRoleNamesAsync"/>
/// against the three shapes ADR 0007 enumerates: multi-role current,
/// expired/future excluded, and empty.
/// </summary>
public class UserRoleRepositoryTests : IntegrationTestBase
{
    private static readonly DateTime AsOf = new(2026, 5, 13, 12, 0, 0, DateTimeKind.Utc);

    private static EffectiveDateRange OpenAt(DateTime fromUtc) => new(fromUtc, null);
    private static EffectiveDateRange ClosedWindow(DateTime fromUtc, DateTime toUtc) => new(fromUtc, toUtc);

    [Fact]
    public async Task MultiRoleUser_ReturnsAllCurrentNamesAsync()
    {
        var userId = Guid.NewGuid();
        var roleQm = new Role(Guid.NewGuid(), "QM", "Quality Manager.");
        var roleAud = new Role(Guid.NewGuid(), "Auditor", "Auditor role.");

        await using (var ctx = NewContext())
        {
            ctx.Users.Add(new User(userId, "alice", "Alice", "h", "s", 1000, false));
            ctx.Roles.AddRange(roleQm, roleAud);
            ctx.UserRoles.AddRange(
                new UserRole(Guid.NewGuid(), userId, roleQm.Id, OpenAt(AsOf.AddDays(-1))),
                new UserRole(Guid.NewGuid(), userId, roleAud.Id, OpenAt(AsOf.AddDays(-1))));
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            var repo = new UserRoleRepository(ctx);
            var names = await repo.GetEffectiveRoleNamesAsync(userId, AsOf, Ct);
            names.Should().BeEquivalentTo("QM", "Auditor");
        }
    }

    [Fact]
    public async Task ExpiredOrFutureUserRoles_AreExcludedAsync()
    {
        var userId = Guid.NewGuid();
        var roleExpired = new Role(Guid.NewGuid(), "ExpiredRole", "Expired.");
        var roleFuture = new Role(Guid.NewGuid(), "FutureRole", "Future.");
        var roleCurrent = new Role(Guid.NewGuid(), "CurrentRole", "Current.");

        await using (var ctx = NewContext())
        {
            ctx.Users.Add(new User(userId, "bob", "Bob", "h", "s", 1000, false));
            ctx.Roles.AddRange(roleExpired, roleFuture, roleCurrent);
            ctx.UserRoles.AddRange(
                new UserRole(Guid.NewGuid(), userId, roleExpired.Id,
                    ClosedWindow(AsOf.AddYears(-2), AsOf.AddYears(-1))),
                new UserRole(Guid.NewGuid(), userId, roleFuture.Id,
                    OpenAt(AsOf.AddDays(14))),
                new UserRole(Guid.NewGuid(), userId, roleCurrent.Id,
                    OpenAt(AsOf.AddDays(-1))));
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            var repo = new UserRoleRepository(ctx);
            var names = await repo.GetEffectiveRoleNamesAsync(userId, AsOf, Ct);
            names.Should().ContainSingle().Which.Should().Be("CurrentRole");
        }
    }

    [Fact]
    public async Task NoRoleUser_ReturnsEmptyNotNullAsync()
    {
        var userId = Guid.NewGuid();
        await using (var ctx = NewContext())
        {
            ctx.Users.Add(new User(userId, "carol", "Carol", "h", "s", 1000, false));
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            var repo = new UserRoleRepository(ctx);
            var names = await repo.GetEffectiveRoleNamesAsync(userId, AsOf, Ct);
            names.Should().NotBeNull();
            names.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task NonUtcAsOfUtc_ThrowsArgumentExceptionAsync()
    {
        await using var ctx = NewContext();
        var repo = new UserRoleRepository(ctx);
        var localTime = new DateTime(2026, 5, 13, 12, 0, 0, DateTimeKind.Local);

        await FluentActions
            .Awaiting(() => repo.GetEffectiveRoleNamesAsync(Guid.NewGuid(), localTime, Ct))
            .Should().ThrowAsync<ArgumentException>().WithMessage("*Utc*");
    }
}
