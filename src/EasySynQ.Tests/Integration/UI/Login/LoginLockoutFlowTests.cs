using AwesomeAssertions;

using EasySynQ.Domain.Entities.Identity;
using EasySynQ.Services.Identity;
using EasySynQ.Tests.Integration.Services;
using EasySynQ.UI.Login;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Moq;

using Xunit;

namespace EasySynQ.Tests.Integration.UI.Login;

/// <summary>
/// End-to-end integration coverage for the SPEC §3.4 / ADR 0006 lockout
/// flow. Drives a real <see cref="LoginViewModel"/> against a real
/// <see cref="AuthenticationService"/> backed by a real SQLite database,
/// using <see cref="EasySynQ.Tests.TestHelpers.FixedClock"/> as the
/// shared clock so the test can advance time past the lockout window
/// instead of waiting fifteen wall-clock minutes.
/// </summary>
/// <remarks>
/// <para>
/// This test was added to close the
/// "Lockout-window check is a known soft spot — covered by ADR 0006 but
/// the runtime behavior isn't yet exercised by an integration test" item
/// in <c>docs/SESSION_NOTES.md</c>. It complements the service-level
/// <c>AuthenticationServiceTests</c> by exercising the same lockout
/// state machine through the view model that the production WPF surface
/// will use.
/// </para>
/// <para>
/// <b>LockedUntilUtc capture.</b> The view model does not expose the
/// <see cref="AuthenticationResult.AccountLocked.LockedUntilUtc"/> value
/// returned by the auth service — only its formatted countdown
/// derivative (<see cref="LoginViewModel.LockoutCountdownDisplay"/>).
/// To advance the clock by an amount derived from behavior rather than
/// configuration (so the test stays meaningful if ADR 0006's window
/// length changes), the test reads
/// <c>User.LockedUntilUtc</c> directly from the database. This is a
/// test-only introspection; production code does not depend on this
/// path.
/// </para>
/// </remarks>
public class LoginLockoutFlowTests : ServiceIntegrationTestBase
{
    [Fact]
    public async Task LockoutFlow_FailedAttemptsThenWindowExpiry_LocksAndRecoversAsync()
    {
        // ---------- Phase 1 — seed an enabled user with a known password.
        await SeedUserAsync("alice", "correct-password");

        // Single scope for the whole flow. Each AuthenticateAsync call
        // operates on the same EF tracked User; the auth service's
        // RegisterFailedLogin / RegisterSuccessfulLogin mutations and
        // SaveChanges round-trips all flow through that one DbContext.
        await using var scope = NewScope();
        var auth = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();

        var logger = new Mock<ILogger<LoginViewModel>>();
        // The [LoggerMessage] source generator on LoginViewModel wraps
        // emits in `if (_logger.IsEnabled(...))`. Stub true so the test
        // does not silently swallow log calls (defensive — this test
        // does not directly verify any log call, but a future refactor
        // that does would otherwise quietly under-assert).
        logger.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);

        var vm = new LoginViewModel(auth, Clock, logger.Object);
        var loginSucceededCount = 0;
        vm.LoginSucceeded += (_, _) => loginSucceededCount++;

        // ---------- Phase 2 — drive to lockout with N consecutive failures.
        //
        // Implementation note: the threshold-crossing attempt (attempt N)
        // writes the lockout to the database AND returns
        // InvalidCredentials. The AccountLocked result is reserved for
        // SUBSEQUENT attempts that find the lockout already active. This
        // matches AuthenticationService.AuthenticateAsync's logic — the
        // failure branch increments the counter, optionally applies the
        // lockout, saves, and returns InvalidCredentials — and the
        // existing AuthenticationServiceTests
        // (Authenticate_FiveConsecutiveFailures_LocksAccountAsync) which
        // also loops MaxFailedAttempts times for failures and then does
        // one additional attempt to see AccountLocked.
        for (var attempt = 1; attempt <= Policy.MaxFailedAttempts; attempt++)
        {
            vm.Username = "alice";
            await vm.SignInCommand.ExecuteAsync("wrong-password");

            vm.ErrorMessage.Should().Be(
                "Invalid username or password.",
                "attempt {0} of {1} returns InvalidCredentials even when it is the one that applies the lockout",
                attempt, Policy.MaxFailedAttempts);
            vm.IsLockedOut.Should().BeFalse(
                "AccountLocked is reserved for attempts that find the lockout already active");
        }

        // Confirm the threshold-crossing attempt did apply the lockout
        // in the database (the user-facing result was InvalidCredentials,
        // but the side effect is the load-bearing one).
        DateTime lockedUntilUtc;
        await using (var ctx = NewContext())
        {
            var seeded = await ctx.Users.SingleAsync(u => u.Username == "alice", Ct);
            seeded.LockedUntilUtc.Should().NotBeNull(
                "the {0}th failure should have called ApplyLockout", Policy.MaxFailedAttempts);
            seeded.FailedLoginCount.Should().Be(Policy.MaxFailedAttempts);
            lockedUntilUtc = seeded.LockedUntilUtc!.Value;
            lockedUntilUtc.Should().Be(
                Clock.UtcNow + Policy.LockoutDuration,
                "ADR 0006 sets the lockout end to now + LockoutDuration on threshold crossing");
        }

        // ---------- Phase 3 — verify the lockout is real and user-visible.
        //
        // The next attempt — the first one to actually receive
        // AccountLocked from the auth service — is what transitions the
        // VM into IsLockedOut + countdown. Three observations:
        //   (a) Drive the VM with the CORRECT password while still
        //       inside the window; AccountLocked propagates to the VM,
        //       which latches IsLockedOut and formats the countdown.
        //       The user is not signed in (LoginSucceeded does not raise).
        //   (b) The error surface reads "Account locked. Try again in
        //       Xm Ys" — the format LoginViewModel.FormatLockoutCountdown
        //       produces.
        //   (c) Call the auth service directly with the CORRECT
        //       password; the raw AuthenticationResult is AccountLocked,
        //       proving the enforcement is service-side and not merely
        //       a VM-level latch.
        loginSucceededCount = 0;
        vm.Username = "alice";
        await vm.SignInCommand.ExecuteAsync("correct-password");

        vm.IsLockedOut.Should().BeTrue(
            "the post-threshold attempt is the first one to receive AccountLocked");
        vm.ErrorMessage.Should().StartWith("Account locked. Try again in");
        vm.LockoutCountdownDisplay.Should().MatchRegex(
            @"^\d+m \d+s$",
            "countdown is formatted as 'Xm Ys' per LoginViewModel.FormatLockoutCountdown");
        loginSucceededCount.Should().Be(0,
            "a locked account cannot succeed even with the correct password");

        var directWhileLocked = await auth.AuthenticateAsync("alice", "correct-password", Ct);
        directWhileLocked.Should().BeOfType<AuthenticationResult.AccountLocked>(
            "the service must enforce the lockout regardless of password correctness");

        // ---------- Phase 4 — expire the window.
        //
        // Advance the clock to one second past the lockout expiry the
        // service reported, so the next attempt sees `lockedUntil > now`
        // as false and falls through to PBKDF2 verification. Computing
        // the target from the captured lockedUntilUtc rather than from
        // Policy.LockoutDuration ensures this test verifies behavior,
        // not configuration — if ADR 0006 raises the window length, the
        // test stays green for the right reason.
        Clock.UtcNow = lockedUntilUtc + TimeSpan.FromSeconds(1);

        // Fresh VM. LoginViewModel.IsLockedOut is a one-way latch within
        // a single VM instance (documented behavior); production opens a
        // new VM after the lockout window expires (new sign-in attempt
        // = new window = new VM). Mirror that here.
        var freshVm = new LoginViewModel(auth, Clock, logger.Object);
        AuthenticatedUserEventArgs? captured = null;
        var freshLoginSucceededCount = 0;
        freshVm.LoginSucceeded += (_, e) =>
        {
            captured = e;
            freshLoginSucceededCount++;
        };

        // ---------- Phase 5 — recover.
        freshVm.Username = "alice";
        await freshVm.SignInCommand.ExecuteAsync("correct-password");

        freshLoginSucceededCount.Should().Be(1,
            "the correct password after the lockout window expires must sign the user in");
        captured.Should().NotBeNull();
        captured!.User.Username.Should().Be("alice");
        freshVm.ErrorMessage.Should().BeNull("a successful sign-in clears the error state");
        freshVm.IsLockedOut.Should().BeFalse(
            "a fresh VM starts unlatched, and the successful sign-in did nothing to relatch it");
    }

    /// <summary>
    /// Seeds an enabled user whose stored hash is computed against the
    /// fast test policy (<see cref="ServiceIntegrationTestBase.TestIterationCount"/>
    /// PBKDF2 iterations). Mirrors the helper in
    /// <see cref="AuthenticationServiceTests"/>.
    /// </summary>
    private async Task SeedUserAsync(string username, string password)
    {
        var hasher = new PasswordHasher(Policy);
        var hashed = hasher.Hash(password);
        await using var ctx = NewContext();
        ctx.Users.Add(new User(
            id: Guid.NewGuid(),
            username: username,
            displayName: username,
            passwordHash: hashed.Hash,
            passwordSalt: hashed.Salt,
            passwordIterationCount: hashed.IterationCount,
            mustChangePassword: false));
        await ctx.SaveChangesAsync(Ct);
    }
}
