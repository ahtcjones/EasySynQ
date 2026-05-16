using AwesomeAssertions;

using EasySynQ.Data.Repositories;
using EasySynQ.Domain.Entities.Identity;
using EasySynQ.Domain.ValueObjects;
using EasySynQ.Tests.Integration.Data.Interceptors;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace EasySynQ.Tests.Integration.Data.Repositories;

public class UserRepositoryTests : InterceptorIntegrationTestBase
{
    [Fact]
    public async Task FindByUsernameAsync_IsCaseInsensitiveAsync()
    {
        var user = new User(Guid.NewGuid(), "Alice", "Alice Smith", "h", "s", 600_000, false);
        await using (var ctx = NewContext())
        {
            ctx.Users.Add(user);
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            var repo = new UserRepository(ctx);

            (await repo.FindByUsernameAsync("alice", Ct))!.Id.Should().Be(user.Id);
            (await repo.FindByUsernameAsync("ALICE", Ct))!.Id.Should().Be(user.Id);
            (await repo.FindByUsernameAsync("AlIcE", Ct))!.Id.Should().Be(user.Id);
        }
    }

    [Fact]
    public async Task FindByUsernameAsync_ReturnsNullForSoftDeletedUserAsync()
    {
        var user = new User(Guid.NewGuid(), "bob", "Bob", "h", "s", 600_000, false);
        await using (var ctx = NewContext())
        {
            ctx.Users.Add(user);
            await ctx.SaveChangesAsync(Ct);

            ctx.Entry(user).Property(nameof(User.IsDeleted)).CurrentValue = true;
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            var repo = new UserRepository(ctx);
            var found = await repo.FindByUsernameAsync("bob", Ct);
            found.Should().BeNull();
        }
    }

    // ─── GetByIdsAsync (C6a) ────────────────────────────────────────

    [Fact]
    public async Task GetByIdsAsync_EmptyInput_ReturnsEmptyAsync()
    {
        await using var ctx = NewContext();
        var repo = new UserRepository(ctx);

        var result = await repo.GetByIdsAsync([], Ct);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByIdsAsync_ReturnsRequestedRowsAsync()
    {
        var alice = new User(Guid.NewGuid(), "alice", "Alice", "h", "s", 600_000, false);
        var bob = new User(Guid.NewGuid(), "bob", "Bob", "h", "s", 600_000, false);
        var carol = new User(Guid.NewGuid(), "carol", "Carol", "h", "s", 600_000, false);
        await using (var ctx = NewContext())
        {
            ctx.Users.AddRange(alice, bob, carol);
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            var repo = new UserRepository(ctx);
            var result = await repo.GetByIdsAsync([alice.Id, carol.Id], Ct);

            result.Should().HaveCount(2);
            result.Select(u => u.Id).Should().BeEquivalentTo([alice.Id, carol.Id]);
        }
    }

    [Fact]
    public async Task GetByIdsAsync_SilentlyDropsMissingIdsAsync()
    {
        var alice = new User(Guid.NewGuid(), "alice2", "Alice", "h", "s", 600_000, false);
        await using (var ctx = NewContext())
        {
            ctx.Users.Add(alice);
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            var repo = new UserRepository(ctx);
            var ghost = Guid.NewGuid();
            var result = await repo.GetByIdsAsync([alice.Id, ghost], Ct);

            result.Should().ContainSingle().Which.Id.Should().Be(alice.Id);
        }
    }

    [Fact]
    public async Task GetByIdsAsync_ExcludesSoftDeletedAsync()
    {
        var alice = new User(Guid.NewGuid(), "alice3", "Alice", "h", "s", 600_000, false);
        var bob = new User(Guid.NewGuid(), "bob3", "Bob", "h", "s", 600_000, false);
        await using (var ctx = NewContext())
        {
            ctx.Users.AddRange(alice, bob);
            await ctx.SaveChangesAsync(Ct);

            ctx.Entry(bob).Property(nameof(User.IsDeleted)).CurrentValue = true;
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            var repo = new UserRepository(ctx);
            var result = await repo.GetByIdsAsync([alice.Id, bob.Id], Ct);

            result.Should().ContainSingle().Which.Id.Should().Be(alice.Id);
        }
    }

    [Fact]
    public async Task GetByIdsAsync_DedupesDuplicateInputIdsAsync()
    {
        var alice = new User(Guid.NewGuid(), "alice4", "Alice", "h", "s", 600_000, false);
        await using (var ctx = NewContext())
        {
            ctx.Users.Add(alice);
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            var repo = new UserRepository(ctx);
            var result = await repo.GetByIdsAsync([alice.Id, alice.Id, alice.Id], Ct);

            result.Should().ContainSingle().Which.Id.Should().Be(alice.Id);
        }
    }

    [Fact]
    public async Task GetByIdsAsync_NullInput_ThrowsArgumentNullExceptionAsync()
    {
        await using var ctx = NewContext();
        var repo = new UserRepository(ctx);

        Func<Task> act = async () => await repo.GetByIdsAsync(null!, Ct);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // ─── GetUsersWithPermissionAsync (C6b) ───────────────────────────

    private static readonly DateTime PermAsOf =
        new(2026, 5, 16, 12, 0, 0, DateTimeKind.Utc);

    private static EffectiveDateRange OpenAt(DateTime fromUtc) => new(fromUtc, null);
    private static EffectiveDateRange ClosedWindow(DateTime fromUtc, DateTime toUtc)
        => new(fromUtc, toUtc);

    [Fact]
    public async Task GetUsersWithPermissionAsync_UnknownPermissionName_ReturnsEmptyAsync()
    {
        await using (var ctx = NewContext())
        {
            ctx.Users.Add(new User(Guid.NewGuid(), "ghost", "G", "h", "s", 1000, false));
            await ctx.SaveChangesAsync(Ct);
        }

        await using var ctx2 = NewContext();
        var repo = new UserRepository(ctx2);
        var result = await repo.GetUsersWithPermissionAsync(
            "Nonexistent.Permission", PermAsOf, Ct);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetUsersWithPermissionAsync_RolePath_ReturnsUserAsync()
    {
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var perm = new Permission(Guid.NewGuid(), "Doc.Review.Test", "review", "Document");

        await using (var ctx = NewContext())
        {
            ctx.Users.Add(new User(userId, "alice", "Alice", "h", "s", 1000, false));
            ctx.Roles.Add(new Role(roleId, "Reviewer", "Reviewer."));
            ctx.Permissions.Add(perm);
            ctx.UserRoles.Add(new UserRole(
                Guid.NewGuid(), userId, roleId, OpenAt(PermAsOf.AddDays(-1))));
            ctx.RolePermissions.Add(new RolePermission(
                Guid.NewGuid(), roleId, perm.Id, OpenAt(PermAsOf.AddDays(-1))));
            await ctx.SaveChangesAsync(Ct);
        }

        await using var ctx2 = NewContext();
        var repo = new UserRepository(ctx2);
        var result = await repo.GetUsersWithPermissionAsync(perm.Name, PermAsOf, Ct);

        result.Should().ContainSingle().Which.Id.Should().Be(userId);
    }

    [Fact]
    public async Task GetUsersWithPermissionAsync_DirectGrantPath_ReturnsUserAsync()
    {
        var userId = Guid.NewGuid();
        var perm = new Permission(Guid.NewGuid(), "Doc.Review.Direct", "direct", "Document");

        await using (var ctx = NewContext())
        {
            ctx.Users.Add(new User(userId, "bob", "Bob", "h", "s", 1000, false));
            ctx.Permissions.Add(perm);
            ctx.UserPermissions.Add(new UserPermission(
                Guid.NewGuid(), userId, perm.Id, OpenAt(PermAsOf.AddDays(-1))));
            await ctx.SaveChangesAsync(Ct);
        }

        await using var ctx2 = NewContext();
        var repo = new UserRepository(ctx2);
        var result = await repo.GetUsersWithPermissionAsync(perm.Name, PermAsOf, Ct);

        result.Should().ContainSingle().Which.Id.Should().Be(userId);
    }

    [Fact]
    public async Task GetUsersWithPermissionAsync_DedupesUserReachedByBothPathsAsync()
    {
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var perm = new Permission(Guid.NewGuid(), "Doc.Review.Both", "both", "Document");

        await using (var ctx = NewContext())
        {
            ctx.Users.Add(new User(userId, "carol", "Carol", "h", "s", 1000, false));
            ctx.Roles.Add(new Role(roleId, "Dual", "Dual."));
            ctx.Permissions.Add(perm);
            ctx.UserRoles.Add(new UserRole(
                Guid.NewGuid(), userId, roleId, OpenAt(PermAsOf.AddDays(-1))));
            ctx.RolePermissions.Add(new RolePermission(
                Guid.NewGuid(), roleId, perm.Id, OpenAt(PermAsOf.AddDays(-1))));
            ctx.UserPermissions.Add(new UserPermission(
                Guid.NewGuid(), userId, perm.Id, OpenAt(PermAsOf.AddDays(-1))));
            await ctx.SaveChangesAsync(Ct);
        }

        await using var ctx2 = NewContext();
        var repo = new UserRepository(ctx2);
        var result = await repo.GetUsersWithPermissionAsync(perm.Name, PermAsOf, Ct);

        result.Should().ContainSingle().Which.Id.Should().Be(userId);
    }

    [Fact]
    public async Task GetUsersWithPermissionAsync_ExcludesSoftDeletedUserAsync()
    {
        var aliveId = Guid.NewGuid();
        var deletedId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var perm = new Permission(Guid.NewGuid(), "Doc.Review.Sd", "sd", "Document");

        await using (var ctx = NewContext())
        {
            var alive = new User(aliveId, "alive", "Alive", "h", "s", 1000, false);
            var deleted = new User(deletedId, "deleted", "Deleted", "h", "s", 1000, false);
            ctx.Users.AddRange(alive, deleted);
            ctx.Roles.Add(new Role(roleId, "R", "R."));
            ctx.Permissions.Add(perm);
            ctx.UserRoles.AddRange(
                new UserRole(Guid.NewGuid(), aliveId, roleId, OpenAt(PermAsOf.AddDays(-1))),
                new UserRole(Guid.NewGuid(), deletedId, roleId, OpenAt(PermAsOf.AddDays(-1))));
            ctx.RolePermissions.Add(new RolePermission(
                Guid.NewGuid(), roleId, perm.Id, OpenAt(PermAsOf.AddDays(-1))));
            await ctx.SaveChangesAsync(Ct);

            ctx.Entry(deleted).Property(nameof(User.IsDeleted)).CurrentValue = true;
            await ctx.SaveChangesAsync(Ct);
        }

        await using var ctx2 = NewContext();
        var repo = new UserRepository(ctx2);
        var result = await repo.GetUsersWithPermissionAsync(perm.Name, PermAsOf, Ct);

        result.Should().ContainSingle().Which.Id.Should().Be(aliveId);
    }

    [Fact]
    public async Task GetUsersWithPermissionAsync_ExpiredRoleAssignmentExcludedAsync()
    {
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var perm = new Permission(Guid.NewGuid(), "Doc.Review.Exp", "exp", "Document");

        await using (var ctx = NewContext())
        {
            ctx.Users.Add(new User(userId, "ex", "Ex", "h", "s", 1000, false));
            ctx.Roles.Add(new Role(roleId, "R", "R."));
            ctx.Permissions.Add(perm);
            ctx.UserRoles.Add(new UserRole(
                Guid.NewGuid(), userId, roleId,
                ClosedWindow(PermAsOf.AddDays(-10), PermAsOf.AddDays(-1))));
            ctx.RolePermissions.Add(new RolePermission(
                Guid.NewGuid(), roleId, perm.Id, OpenAt(PermAsOf.AddDays(-10))));
            await ctx.SaveChangesAsync(Ct);
        }

        await using var ctx2 = NewContext();
        var repo = new UserRepository(ctx2);
        var result = await repo.GetUsersWithPermissionAsync(perm.Name, PermAsOf, Ct);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetUsersWithPermissionAsync_ExpiredDirectGrantExcludedAsync()
    {
        var userId = Guid.NewGuid();
        var perm = new Permission(Guid.NewGuid(), "Doc.Review.ExpD", "expd", "Document");

        await using (var ctx = NewContext())
        {
            ctx.Users.Add(new User(userId, "ed", "Ed", "h", "s", 1000, false));
            ctx.Permissions.Add(perm);
            ctx.UserPermissions.Add(new UserPermission(
                Guid.NewGuid(), userId, perm.Id,
                ClosedWindow(PermAsOf.AddDays(-10), PermAsOf.AddDays(-1))));
            await ctx.SaveChangesAsync(Ct);
        }

        await using var ctx2 = NewContext();
        var repo = new UserRepository(ctx2);
        var result = await repo.GetUsersWithPermissionAsync(perm.Name, PermAsOf, Ct);

        result.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetUsersWithPermissionAsync_NullOrWhitespacePermissionName_ThrowsAsync(string? name)
    {
        await using var ctx = NewContext();
        var repo = new UserRepository(ctx);

        Func<Task> act = async () =>
            await repo.GetUsersWithPermissionAsync(name!, PermAsOf, Ct);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetUsersWithPermissionAsync_NonUtcAsOf_ThrowsAsync()
    {
        await using var ctx = NewContext();
        var repo = new UserRepository(ctx);
        var localKind = new DateTime(2026, 5, 16, 12, 0, 0, DateTimeKind.Local);

        Func<Task> act = async () =>
            await repo.GetUsersWithPermissionAsync("Doc.Review", localKind, Ct);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*asOfUtc*Utc*");
    }
}
