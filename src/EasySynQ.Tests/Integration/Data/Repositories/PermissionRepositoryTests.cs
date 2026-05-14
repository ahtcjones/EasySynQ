using AwesomeAssertions;

using EasySynQ.Data.Repositories;
using EasySynQ.Domain.Entities.Identity;
using EasySynQ.Domain.ValueObjects;
using EasySynQ.Tests.Integration.Data;

using Xunit;

namespace EasySynQ.Tests.Integration.Data.Repositories;

/// <summary>
/// Exercises <see cref="PermissionRepository.GetEffectivePermissionNamesForUserAsync"/>
/// against every shape ADR 0007 enumerates in its Required Tests
/// section: role-derived only, mixed, deduped, expired/future excluded,
/// and empty-result.
/// </summary>
public class PermissionRepositoryTests : IntegrationTestBase
{
    private static readonly DateTime AsOf = new(2026, 5, 13, 12, 0, 0, DateTimeKind.Utc);

    private static EffectiveDateRange OpenAt(DateTime fromUtc) => new(fromUtc, null);
    private static EffectiveDateRange ClosedWindow(DateTime fromUtc, DateTime toUtc) => new(fromUtc, toUtc);

    [Fact]
    public async Task RoleWithThreePermissions_ReturnsThreeNamesAsync()
    {
        var (userId, roleId) = (Guid.NewGuid(), Guid.NewGuid());
        var permA = new Permission(Guid.NewGuid(), "Doc.A", "A", "Document");
        var permB = new Permission(Guid.NewGuid(), "Doc.B", "B", "Document");
        var permC = new Permission(Guid.NewGuid(), "Doc.C", "C", "Document");

        await using (var ctx = NewContext())
        {
            ctx.Users.Add(new User(userId, "alice", "Alice", "h", "s", 1000, false));
            ctx.Roles.Add(new Role(roleId, "QM", "Quality Manager."));
            ctx.Permissions.AddRange(permA, permB, permC);
            ctx.UserRoles.Add(new UserRole(Guid.NewGuid(), userId, roleId, OpenAt(AsOf.AddDays(-1))));
            ctx.RolePermissions.AddRange(
                new RolePermission(Guid.NewGuid(), roleId, permA.Id, OpenAt(AsOf.AddDays(-1))),
                new RolePermission(Guid.NewGuid(), roleId, permB.Id, OpenAt(AsOf.AddDays(-1))),
                new RolePermission(Guid.NewGuid(), roleId, permC.Id, OpenAt(AsOf.AddDays(-1))));
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            var repo = new PermissionRepository(ctx);
            var names = await repo.GetEffectivePermissionNamesForUserAsync(userId, AsOf, Ct);
            names.Should().BeEquivalentTo("Doc.A", "Doc.B", "Doc.C");
        }
    }

    [Fact]
    public async Task RolePlusDirectGrant_UnionsBothBranchesAsync()
    {
        var (userId, roleId) = (Guid.NewGuid(), Guid.NewGuid());
        var rolePerm = new Permission(Guid.NewGuid(), "Role.P", "role perm", "Document");
        var directPerm = new Permission(Guid.NewGuid(), "Direct.P", "direct perm", "Production");

        await using (var ctx = NewContext())
        {
            ctx.Users.Add(new User(userId, "bob", "Bob", "h", "s", 1000, false));
            ctx.Roles.Add(new Role(roleId, "LT", "Lab Tech."));
            ctx.Permissions.AddRange(rolePerm, directPerm);
            ctx.UserRoles.Add(new UserRole(Guid.NewGuid(), userId, roleId, OpenAt(AsOf.AddDays(-1))));
            ctx.RolePermissions.Add(new RolePermission(Guid.NewGuid(), roleId, rolePerm.Id, OpenAt(AsOf.AddDays(-1))));
            ctx.UserPermissions.Add(new UserPermission(Guid.NewGuid(), userId, directPerm.Id, OpenAt(AsOf.AddDays(-1))));
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            var repo = new PermissionRepository(ctx);
            var names = await repo.GetEffectivePermissionNamesForUserAsync(userId, AsOf, Ct);
            names.Should().BeEquivalentTo("Role.P", "Direct.P");
        }
    }

    [Fact]
    public async Task SamePermissionViaBothPaths_ReturnsSingleEntryAsync()
    {
        var (userId, roleId) = (Guid.NewGuid(), Guid.NewGuid());
        var shared = new Permission(Guid.NewGuid(), "Shared.X", "shared", "Document");

        await using (var ctx = NewContext())
        {
            ctx.Users.Add(new User(userId, "carol", "Carol", "h", "s", 1000, false));
            ctx.Roles.Add(new Role(roleId, "QM", "Quality Manager."));
            ctx.Permissions.Add(shared);
            ctx.UserRoles.Add(new UserRole(Guid.NewGuid(), userId, roleId, OpenAt(AsOf.AddDays(-1))));
            ctx.RolePermissions.Add(new RolePermission(Guid.NewGuid(), roleId, shared.Id, OpenAt(AsOf.AddDays(-1))));
            ctx.UserPermissions.Add(new UserPermission(Guid.NewGuid(), userId, shared.Id, OpenAt(AsOf.AddDays(-1))));
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            var repo = new PermissionRepository(ctx);
            var names = await repo.GetEffectivePermissionNamesForUserAsync(userId, AsOf, Ct);
            names.Should().ContainSingle().Which.Should().Be("Shared.X");
        }
    }

    [Fact]
    public async Task ExpiredRolePermission_IsExcludedAsync()
    {
        var (userId, roleId) = (Guid.NewGuid(), Guid.NewGuid());
        var perm = new Permission(Guid.NewGuid(), "Expired.RP", "expired role-perm", "Document");

        await using (var ctx = NewContext())
        {
            ctx.Users.Add(new User(userId, "dave", "Dave", "h", "s", 1000, false));
            ctx.Roles.Add(new Role(roleId, "Operator", "Operator."));
            ctx.Permissions.Add(perm);
            ctx.UserRoles.Add(new UserRole(Guid.NewGuid(), userId, roleId, OpenAt(AsOf.AddYears(-2))));
            // RolePermission expired a month before asOf
            ctx.RolePermissions.Add(new RolePermission(
                Guid.NewGuid(), roleId, perm.Id,
                ClosedWindow(AsOf.AddYears(-1), AsOf.AddMonths(-1))));
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            var repo = new PermissionRepository(ctx);
            var names = await repo.GetEffectivePermissionNamesForUserAsync(userId, AsOf, Ct);
            names.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task FutureEffectiveRolePermission_IsExcludedAsync()
    {
        var (userId, roleId) = (Guid.NewGuid(), Guid.NewGuid());
        var perm = new Permission(Guid.NewGuid(), "Future.RP", "future role-perm", "Document");

        await using (var ctx = NewContext())
        {
            ctx.Users.Add(new User(userId, "ed", "Ed", "h", "s", 1000, false));
            ctx.Roles.Add(new Role(roleId, "Operator", "Operator."));
            ctx.Permissions.Add(perm);
            ctx.UserRoles.Add(new UserRole(Guid.NewGuid(), userId, roleId, OpenAt(AsOf.AddDays(-1))));
            // Pre-scheduled RolePermission — starts a week after asOf
            ctx.RolePermissions.Add(new RolePermission(
                Guid.NewGuid(), roleId, perm.Id, OpenAt(AsOf.AddDays(7))));
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            var repo = new PermissionRepository(ctx);
            var names = await repo.GetEffectivePermissionNamesForUserAsync(userId, AsOf, Ct);
            names.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task ExpiredUserPermission_IsExcludedAsync()
    {
        var userId = Guid.NewGuid();
        var perm = new Permission(Guid.NewGuid(), "Expired.UP", "expired user-perm", "Document");

        await using (var ctx = NewContext())
        {
            ctx.Users.Add(new User(userId, "fay", "Fay", "h", "s", 1000, false));
            ctx.Permissions.Add(perm);
            ctx.UserPermissions.Add(new UserPermission(
                Guid.NewGuid(), userId, perm.Id,
                ClosedWindow(AsOf.AddYears(-1), AsOf.AddMonths(-1))));
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            var repo = new PermissionRepository(ctx);
            var names = await repo.GetEffectivePermissionNamesForUserAsync(userId, AsOf, Ct);
            names.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task FutureEffectiveUserPermission_IsExcludedAsync()
    {
        var userId = Guid.NewGuid();
        var perm = new Permission(Guid.NewGuid(), "Future.UP", "future user-perm", "Document");

        await using (var ctx = NewContext())
        {
            ctx.Users.Add(new User(userId, "gail", "Gail", "h", "s", 1000, false));
            ctx.Permissions.Add(perm);
            ctx.UserPermissions.Add(new UserPermission(
                Guid.NewGuid(), userId, perm.Id, OpenAt(AsOf.AddDays(7))));
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            var repo = new PermissionRepository(ctx);
            var names = await repo.GetEffectivePermissionNamesForUserAsync(userId, AsOf, Ct);
            names.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task NoRoleAndNoDirectGrants_ReturnsEmptyNotNullAsync()
    {
        var userId = Guid.NewGuid();
        await using (var ctx = NewContext())
        {
            ctx.Users.Add(new User(userId, "hank", "Hank", "h", "s", 1000, false));
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            var repo = new PermissionRepository(ctx);
            var names = await repo.GetEffectivePermissionNamesForUserAsync(userId, AsOf, Ct);
            names.Should().NotBeNull();
            names.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task NonUtcAsOfUtc_ThrowsArgumentExceptionAsync()
    {
        // Defensive — repository explicitly rejects non-UTC inputs so the
        // caller can't accidentally pass a local-time DateTime through
        // and silently get the wrong filter window.
        await using var ctx = NewContext();
        var repo = new PermissionRepository(ctx);
        var localTime = new DateTime(2026, 5, 13, 12, 0, 0, DateTimeKind.Local);

        await FluentActions
            .Awaiting(() => repo.GetEffectivePermissionNamesForUserAsync(Guid.NewGuid(), localTime, Ct))
            .Should().ThrowAsync<ArgumentException>().WithMessage("*Utc*");
    }
}
