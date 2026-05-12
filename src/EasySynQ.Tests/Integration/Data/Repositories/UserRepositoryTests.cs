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
}
