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

    // ─── GetEffectiveRolePermissionMapForUserAsync (ADR 0009 C4) ───

    [Fact]
    public async Task RolePermissionMap_OneRoleThreePerms_ReturnsMapWithOneKeyAndThreeValuesAsync()
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
            var map = await repo.GetEffectiveRolePermissionMapForUserAsync(userId, AsOf, Ct);

            map.Should().HaveCount(1);
            map.Should().ContainKey("QM");
            map["QM"].Should().BeEquivalentTo("Doc.A", "Doc.B", "Doc.C");
        }
    }

    [Fact]
    public async Task RolePermissionMap_TwoRolesDistinctPerms_ReturnsBothKeysAsync()
    {
        var userId = Guid.NewGuid();
        var roleA = Guid.NewGuid();
        var roleB = Guid.NewGuid();
        var permA = new Permission(Guid.NewGuid(), "A.Read", "a", "X");
        var permB = new Permission(Guid.NewGuid(), "B.Write", "b", "X");

        await using (var ctx = NewContext())
        {
            ctx.Users.Add(new User(userId, "alice", "Alice", "h", "s", 1000, false));
            ctx.Roles.AddRange(
                new Role(roleA, "RoleA", "A"),
                new Role(roleB, "RoleB", "B"));
            ctx.Permissions.AddRange(permA, permB);
            ctx.UserRoles.AddRange(
                new UserRole(Guid.NewGuid(), userId, roleA, OpenAt(AsOf.AddDays(-1))),
                new UserRole(Guid.NewGuid(), userId, roleB, OpenAt(AsOf.AddDays(-1))));
            ctx.RolePermissions.AddRange(
                new RolePermission(Guid.NewGuid(), roleA, permA.Id, OpenAt(AsOf.AddDays(-1))),
                new RolePermission(Guid.NewGuid(), roleB, permB.Id, OpenAt(AsOf.AddDays(-1))));
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            var repo = new PermissionRepository(ctx);
            var map = await repo.GetEffectiveRolePermissionMapForUserAsync(userId, AsOf, Ct);

            map.Keys.Should().BeEquivalentTo("RoleA", "RoleB");
            map["RoleA"].Should().BeEquivalentTo("A.Read");
            map["RoleB"].Should().BeEquivalentTo("B.Write");
        }
    }

    [Fact]
    public async Task RolePermissionMap_OverlappingPermissions_AppearsUnderEachRoleAsync()
    {
        // Both roles hold the same permission. Each role's value list
        // contains it; client-side dedup is per-role, NOT cross-role
        // (the dictionary keys preserve the per-role attribution).
        var userId = Guid.NewGuid();
        var roleA = Guid.NewGuid();
        var roleB = Guid.NewGuid();
        var sharedPerm = new Permission(Guid.NewGuid(), "Shared.P", "shared", "X");

        await using (var ctx = NewContext())
        {
            ctx.Users.Add(new User(userId, "alice", "Alice", "h", "s", 1000, false));
            ctx.Roles.AddRange(
                new Role(roleA, "RoleA", "A"),
                new Role(roleB, "RoleB", "B"));
            ctx.Permissions.Add(sharedPerm);
            ctx.UserRoles.AddRange(
                new UserRole(Guid.NewGuid(), userId, roleA, OpenAt(AsOf.AddDays(-1))),
                new UserRole(Guid.NewGuid(), userId, roleB, OpenAt(AsOf.AddDays(-1))));
            ctx.RolePermissions.AddRange(
                new RolePermission(Guid.NewGuid(), roleA, sharedPerm.Id, OpenAt(AsOf.AddDays(-1))),
                new RolePermission(Guid.NewGuid(), roleB, sharedPerm.Id, OpenAt(AsOf.AddDays(-1))));
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            var repo = new PermissionRepository(ctx);
            var map = await repo.GetEffectiveRolePermissionMapForUserAsync(userId, AsOf, Ct);

            map["RoleA"].Should().BeEquivalentTo("Shared.P");
            map["RoleB"].Should().BeEquivalentTo("Shared.P");
        }
    }

    [Fact]
    public async Task RolePermissionMap_NoRoles_ReturnsEmptyDictAsync()
    {
        var userId = Guid.NewGuid();
        await using (var ctx = NewContext())
        {
            ctx.Users.Add(new User(userId, "alice", "Alice", "h", "s", 1000, false));
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            var repo = new PermissionRepository(ctx);
            var map = await repo.GetEffectiveRolePermissionMapForUserAsync(userId, AsOf, Ct);

            map.Should().NotBeNull();
            map.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task RolePermissionMap_ExpiredRolePermission_ExcludedAsync()
    {
        var (userId, roleId) = (Guid.NewGuid(), Guid.NewGuid());
        var permLive = new Permission(Guid.NewGuid(), "Live.P", "live", "X");
        var permExpired = new Permission(Guid.NewGuid(), "Expired.P", "expired", "X");

        await using (var ctx = NewContext())
        {
            ctx.Users.Add(new User(userId, "alice", "Alice", "h", "s", 1000, false));
            ctx.Roles.Add(new Role(roleId, "QM", "."));
            ctx.Permissions.AddRange(permLive, permExpired);
            ctx.UserRoles.Add(new UserRole(Guid.NewGuid(), userId, roleId, OpenAt(AsOf.AddDays(-1))));
            ctx.RolePermissions.AddRange(
                new RolePermission(Guid.NewGuid(), roleId, permLive.Id, OpenAt(AsOf.AddDays(-1))),
                new RolePermission(Guid.NewGuid(), roleId, permExpired.Id,
                    ClosedWindow(AsOf.AddDays(-30), AsOf.AddDays(-7))));
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            var repo = new PermissionRepository(ctx);
            var map = await repo.GetEffectiveRolePermissionMapForUserAsync(userId, AsOf, Ct);

            map["QM"].Should().BeEquivalentTo("Live.P");
        }
    }

    [Fact]
    public async Task RolePermissionMap_DirectUserPermission_NotIncludedAsync()
    {
        // Per ADR 0009 — UserPermission rows are NOT in the result;
        // only role-derived permissions are. The user has both a
        // role-derived perm and a direct perm; only the role-derived
        // one appears in the map.
        var (userId, roleId) = (Guid.NewGuid(), Guid.NewGuid());
        var rolePerm = new Permission(Guid.NewGuid(), "Role.P", "rp", "X");
        var directPerm = new Permission(Guid.NewGuid(), "Direct.P", "dp", "X");

        await using (var ctx = NewContext())
        {
            ctx.Users.Add(new User(userId, "alice", "Alice", "h", "s", 1000, false));
            ctx.Roles.Add(new Role(roleId, "QM", "."));
            ctx.Permissions.AddRange(rolePerm, directPerm);
            ctx.UserRoles.Add(new UserRole(Guid.NewGuid(), userId, roleId, OpenAt(AsOf.AddDays(-1))));
            ctx.RolePermissions.Add(
                new RolePermission(Guid.NewGuid(), roleId, rolePerm.Id, OpenAt(AsOf.AddDays(-1))));
            ctx.UserPermissions.Add(
                new UserPermission(Guid.NewGuid(), userId, directPerm.Id, OpenAt(AsOf.AddDays(-1))));
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            var repo = new PermissionRepository(ctx);
            var map = await repo.GetEffectiveRolePermissionMapForUserAsync(userId, AsOf, Ct);

            map.Should().HaveCount(1);
            map["QM"].Should().BeEquivalentTo("Role.P");
            map["QM"].Should().NotContain("Direct.P");
        }
    }

    [Fact]
    public async Task RolePermissionMap_NonUtcAsOfUtc_ThrowsArgumentExceptionAsync()
    {
        await using var ctx = NewContext();
        var repo = new PermissionRepository(ctx);
        var localTime = new DateTime(2026, 5, 13, 12, 0, 0, DateTimeKind.Local);

        await FluentActions
            .Awaiting(() => repo.GetEffectiveRolePermissionMapForUserAsync(Guid.NewGuid(), localTime, Ct))
            .Should().ThrowAsync<ArgumentException>().WithMessage("*Utc*");
    }
}
