using AwesomeAssertions;

using EasySynQ.Domain.Entities.Identity;
using EasySynQ.Services.Abstractions;
using EasySynQ.Services.Identity;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace EasySynQ.Tests.Integration.Services;

public class AuthenticationServiceTests : ServiceIntegrationTestBase
{
    [Fact]
    public async Task Authenticate_WhenNoUsersExist_ReturnsFirstRunBootstrapAsync()
    {
        await using var scope = NewScope();
        var auth = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();

        var result = await auth.AuthenticateAsync("anyone", "anything", Ct);

        result.Should().BeOfType<AuthenticationResult.FirstRunBootstrap>();
    }

    [Fact]
    public async Task Bootstrap_CreatesAdministrator_AndSubsequentAuthSucceedsAsync()
    {
        // Bootstrap.
        await using (var scope = NewScope())
        {
            var auth = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
            var admin = await auth.CreateBootstrapAdministratorAsync(
                username: "admin",
                password: "bootstrap-pw",
                displayName: "Administrator",
                cancellationToken: Ct);

            admin.Username.Should().Be("admin");
            admin.MustChangePassword.Should().BeFalse();
        }

        // Subsequent authenticate succeeds against the bootstrap user.
        await using (var scope = NewScope())
        {
            var auth = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
            var result = await auth.AuthenticateAsync("admin", "bootstrap-pw", Ct);

            var success = result.Should().BeOfType<AuthenticationResult.Success>().Subject;
            success.User.Username.Should().Be("admin");
            success.RequiresPasswordChange.Should().BeFalse();
        }
    }

    [Fact]
    public async Task Bootstrap_OnceAUserExists_ThrowsInvalidOperationAsync()
    {
        await SeedUserAsync("first", "password");

        await using var scope = NewScope();
        var auth = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();

        var act = async () => await auth.CreateBootstrapAdministratorAsync(
            "second", "password", "Second", Ct);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Bootstrap is only available when no users exist*");
    }

    [Fact]
    public async Task Authenticate_UnknownUsername_ReturnsInvalidCredentialsAsync()
    {
        await SeedUserAsync("alice", "password");

        await using var scope = NewScope();
        var auth = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();

        var result = await auth.AuthenticateAsync("nobody", "anything", Ct);
        result.Should().BeOfType<AuthenticationResult.InvalidCredentials>();
    }

    [Fact]
    public async Task Authenticate_WrongPassword_ReturnsInvalidCredentialsAsync()
    {
        await SeedUserAsync("alice", "password");

        await using var scope = NewScope();
        var auth = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();

        var result = await auth.AuthenticateAsync("alice", "wrong-pw", Ct);
        result.Should().BeOfType<AuthenticationResult.InvalidCredentials>();
    }

    [Fact]
    public async Task Authenticate_FiveConsecutiveFailures_LocksAccountAsync()
    {
        await SeedUserAsync("alice", "password");

        await using var scope = NewScope();
        var auth = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();

        for (var i = 0; i < Policy.MaxFailedAttempts; i++)
        {
            (await auth.AuthenticateAsync("alice", "wrong", Ct))
                .Should().BeOfType<AuthenticationResult.InvalidCredentials>();
        }

        // Sixth attempt — even with the right password — returns
        // AccountLocked, with a LockedUntilUtc in the future.
        var locked = await auth.AuthenticateAsync("alice", "password", Ct);
        var lockedResult = locked.Should().BeOfType<AuthenticationResult.AccountLocked>().Subject;
        lockedResult.LockedUntilUtc.Should().Be(Clock.UtcNow + Policy.LockoutDuration);
    }

    [Fact]
    public async Task Authenticate_SuccessAfterPartialFailures_ResetsCounterAsync()
    {
        var userId = await SeedUserAsync("alice", "password");

        await using (var scope = NewScope())
        {
            var auth = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
            // Two failures (under the threshold).
            await auth.AuthenticateAsync("alice", "wrong", Ct);
            await auth.AuthenticateAsync("alice", "wrong", Ct);
        }

        // Read FailedLoginCount mid-way to confirm partial state.
        await using (var ctx = NewContext())
        {
            var u = await ctx.Users.SingleAsync(u => u.Id == userId, Ct);
            u.FailedLoginCount.Should().Be(2);
        }

        // Successful auth.
        await using (var scope = NewScope())
        {
            var auth = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
            (await auth.AuthenticateAsync("alice", "password", Ct))
                .Should().BeOfType<AuthenticationResult.Success>();
        }

        // Counter back to zero, lockout cleared, last-login stamped.
        await using (var ctx = NewContext())
        {
            var u = await ctx.Users.SingleAsync(u => u.Id == userId, Ct);
            u.FailedLoginCount.Should().Be(0);
            u.LockedUntilUtc.Should().BeNull();
            u.LastLoginUtc.Should().Be(Clock.UtcNow);
        }
    }

    [Fact]
    public async Task Authenticate_AfterLockoutExpires_EvaluatesNormallyAsync()
    {
        await SeedUserAsync("alice", "password");

        await using (var scope = NewScope())
        {
            var auth = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
            for (var i = 0; i < Policy.MaxFailedAttempts; i++)
            {
                await auth.AuthenticateAsync("alice", "wrong", Ct);
            }
        }

        // While locked, even right password is rejected.
        await using (var scope = NewScope())
        {
            var auth = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
            (await auth.AuthenticateAsync("alice", "password", Ct))
                .Should().BeOfType<AuthenticationResult.AccountLocked>();
        }

        // Advance the clock past the lockout window.
        Clock.UtcNow = Clock.UtcNow + Policy.LockoutDuration + TimeSpan.FromSeconds(1);

        // Now the right password succeeds.
        await using (var scope = NewScope())
        {
            var auth = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
            (await auth.AuthenticateAsync("alice", "password", Ct))
                .Should().BeOfType<AuthenticationResult.Success>();
        }
    }

    [Fact]
    public async Task Authenticate_DisabledAccount_ReturnsAccountDisabled_RegardlessOfPasswordAsync()
    {
        var userId = await SeedUserAsync("alice", "password");

        // Flip IsDisabled directly via the entry API (no admin service yet).
        await using (var ctx = NewContext())
        {
            var u = await ctx.Users.SingleAsync(u => u.Id == userId, Ct);
            ctx.Entry(u).Property(nameof(User.IsDisabled)).CurrentValue = true;
            await ctx.SaveChangesAsync(Ct);
        }

        await using var scope = NewScope();
        var auth = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();

        // Right password.
        (await auth.AuthenticateAsync("alice", "password", Ct))
            .Should().BeOfType<AuthenticationResult.AccountDisabled>();
        // Wrong password — same response.
        (await auth.AuthenticateAsync("alice", "wrong", Ct))
            .Should().BeOfType<AuthenticationResult.AccountDisabled>();
    }

    [Fact]
    public async Task Authenticate_RehashesSilently_WhenPolicyIterationCountRoseAsync()
    {
        // Hash the user's password under iteration count 500 — below
        // the test policy's 1000.
        var lowHasher = new PasswordHasher(new PasswordPolicy(
            minimumLength: ServiceIntegrationTestBase.TestMinimumLength,
            currentIterationCount: 500,
            maxFailedAttempts: 5,
            lockoutDuration: TimeSpan.FromMinutes(15)));
        var lowHashed = lowHasher.Hash("password");

        var userId = Guid.NewGuid();
        await using (var ctx = NewContext())
        {
            ctx.Users.Add(new User(
                userId, "alice", "Alice",
                lowHashed.Hash, lowHashed.Salt, lowHashed.IterationCount,
                mustChangePassword: false));
            await ctx.SaveChangesAsync(Ct);
        }

        // Authenticate with the right password.
        await using (var scope = NewScope())
        {
            var auth = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
            (await auth.AuthenticateAsync("alice", "password", Ct))
                .Should().BeOfType<AuthenticationResult.Success>();
        }

        // Stored hash and iteration count are now upgraded to the
        // current policy. PasswordHash and PasswordSalt have changed
        // because rehashing produces fresh salt.
        await using (var ctx = NewContext())
        {
            var u = await ctx.Users.SingleAsync(u => u.Id == userId, Ct);
            u.PasswordIterationCount.Should().Be(ServiceIntegrationTestBase.TestIterationCount);
            u.PasswordHash.Should().NotBe(lowHashed.Hash);
            u.PasswordSalt.Should().NotBe(lowHashed.Salt);
        }
    }

    [Fact]
    public async Task ChangePassword_RequiresCurrentPasswordAsync()
    {
        var userId = await SeedUserAsync("alice", "current-pw");

        await using var scope = NewScope();
        var auth = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();

        var ok = await auth.ChangePasswordAsync(userId, "wrong", "new-pw", Ct);
        ok.Should().BeFalse();

        // Old password still works.
        (await auth.AuthenticateAsync("alice", "current-pw", Ct))
            .Should().BeOfType<AuthenticationResult.Success>();
    }

    [Fact]
    public async Task ChangePassword_OnSuccess_ClearsMustChangePasswordAsync()
    {
        // Seed a user with MustChangePassword = true (admin reset shape).
        var hasher = new PasswordHasher(Policy);
        var hashed = hasher.Hash("temp-pw");
        var userId = Guid.NewGuid();
        await using (var ctx = NewContext())
        {
            ctx.Users.Add(new User(
                userId, "alice", "Alice",
                hashed.Hash, hashed.Salt, hashed.IterationCount,
                mustChangePassword: true));
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var scope = NewScope())
        {
            var auth = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
            var ok = await auth.ChangePasswordAsync(userId, "temp-pw", "new-pw", Ct);
            ok.Should().BeTrue();
        }

        // MustChangePassword cleared; new password works.
        await using (var ctx = NewContext())
        {
            var u = await ctx.Users.SingleAsync(u => u.Id == userId, Ct);
            u.MustChangePassword.Should().BeFalse();
        }

        await using (var scope = NewScope())
        {
            var auth = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
            (await auth.AuthenticateAsync("alice", "new-pw", Ct))
                .Should().BeOfType<AuthenticationResult.Success>();
        }
    }

    /// <summary>
    /// Seeds a user with the supplied plaintext password hashed under
    /// the test policy. Returns the user's id.
    /// </summary>
    private async Task<Guid> SeedUserAsync(string username, string password)
    {
        var hasher = new PasswordHasher(Policy);
        var hashed = hasher.Hash(password);
        var userId = Guid.NewGuid();
        await using var ctx = NewContext();
        ctx.Users.Add(new User(
            userId, username, username,
            hashed.Hash, hashed.Salt, hashed.IterationCount,
            mustChangePassword: false));
        await ctx.SaveChangesAsync(Ct);
        return userId;
    }
}
