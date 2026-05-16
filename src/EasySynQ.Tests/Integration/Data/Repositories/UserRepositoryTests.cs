using AwesomeAssertions;

using EasySynQ.Data.Repositories;
using EasySynQ.Domain.Entities.Identity;
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
}
