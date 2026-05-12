using AwesomeAssertions;

using EasySynQ.Domain.Entities.Identity;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace EasySynQ.Tests.Integration.Data.Interceptors;

public class StandardFieldsTests : InterceptorIntegrationTestBase
{
    [Fact]
    public async Task Insert_StampsAllAuditFieldsAndRowVersionAsync()
    {
        var userId = Guid.NewGuid();
        CurrentUser.UserId = userId;
        Clock.UtcNow = new DateTime(2026, 5, 12, 10, 0, 0, DateTimeKind.Utc);

        var user = new User(Guid.NewGuid(), "alice", "Alice", "h", "s", 600_000, false);
        await using var ctx = NewContext();
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync(Ct);

        user.CreatedBy.Should().Be(userId.ToString());
        user.CreatedUtc.Should().Be(Clock.UtcNow);
        user.ModifiedBy.Should().Be(userId.ToString());
        user.ModifiedUtc.Should().Be(Clock.UtcNow);
        user.RowVersion.Should().NotBeEmpty();
        user.RowVersion.Length.Should().Be(8);
    }

    [Fact]
    public async Task Insert_WithUnauthenticatedUser_StampsSystemActorAsync()
    {
        // No CurrentUser.UserId set — interceptor falls back to "system".
        Clock.UtcNow = new DateTime(2026, 5, 12, 10, 0, 0, DateTimeKind.Utc);

        var user = new User(Guid.NewGuid(), "boot", "Boot User", "h", "s", 600_000, false);
        await using var ctx = NewContext();
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync(Ct);

        user.CreatedBy.Should().Be("system");
        user.ModifiedBy.Should().Be("system");
    }

    [Fact]
    public async Task Update_StampsModifiedFields_PreservesCreated_RotatesRowVersionAsync()
    {
        var initialUser = Guid.NewGuid();
        CurrentUser.UserId = initialUser;
        var insertTime = new DateTime(2026, 5, 12, 10, 0, 0, DateTimeKind.Utc);
        Clock.UtcNow = insertTime;

        var user = new User(Guid.NewGuid(), "alice", "Alice", "h", "s", 600_000, false);
        await using (var ctx = NewContext())
        {
            ctx.Users.Add(user);
            await ctx.SaveChangesAsync(Ct);
        }

        var originalCreatedBy = user.CreatedBy;
        var originalCreatedUtc = user.CreatedUtc;
        var originalRowVersionB64 = Convert.ToBase64String(user.RowVersion);

        // Advance clock + change actor; modify the user.
        var updateTime = insertTime.AddMinutes(5);
        Clock.UtcNow = updateTime;
        var updaterUser = Guid.NewGuid();
        CurrentUser.UserId = updaterUser;

        await using (var ctx = NewContext())
        {
            var loaded = await ctx.Users.SingleAsync(u => u.Id == user.Id, Ct);
            ctx.Entry(loaded).Property(nameof(User.MustChangePassword)).CurrentValue = true;
            await ctx.SaveChangesAsync(Ct);

            loaded.CreatedBy.Should().Be(originalCreatedBy);
            loaded.CreatedUtc.Should().Be(originalCreatedUtc);
            loaded.ModifiedBy.Should().Be(updaterUser.ToString());
            loaded.ModifiedUtc.Should().Be(updateTime);
            Convert.ToBase64String(loaded.RowVersion).Should().NotBe(originalRowVersionB64);
        }
    }

    [Fact]
    public async Task SoftDelete_StampsModifiedFields_BumpsRowVersionAsync()
    {
        CurrentUser.UserId = Guid.NewGuid();
        Clock.UtcNow = new DateTime(2026, 5, 12, 10, 0, 0, DateTimeKind.Utc);

        var user = new User(Guid.NewGuid(), "bob", "Bob", "h", "s", 600_000, false);
        await using (var ctx = NewContext())
        {
            ctx.Users.Add(user);
            await ctx.SaveChangesAsync(Ct);
        }

        var originalRowVersionB64 = Convert.ToBase64String(user.RowVersion);

        var deleteTime = Clock.UtcNow.AddMinutes(10);
        Clock.UtcNow = deleteTime;
        var deleter = Guid.NewGuid();
        CurrentUser.UserId = deleter;

        await using (var ctx = NewContext())
        {
            var loaded = await ctx.Users.SingleAsync(u => u.Id == user.Id, Ct);
            ctx.Entry(loaded).Property(nameof(User.IsDeleted)).CurrentValue = true;
            await ctx.SaveChangesAsync(Ct);

            loaded.ModifiedBy.Should().Be(deleter.ToString());
            loaded.ModifiedUtc.Should().Be(deleteTime);
            Convert.ToBase64String(loaded.RowVersion).Should().NotBe(originalRowVersionB64);
        }
    }
}
