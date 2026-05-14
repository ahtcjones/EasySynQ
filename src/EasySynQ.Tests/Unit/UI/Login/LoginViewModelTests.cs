using AwesomeAssertions;

using EasySynQ.Domain.Entities.Identity;
using EasySynQ.Services.Identity;
using EasySynQ.Services.Time;
using EasySynQ.UI.Login;

using Microsoft.Extensions.Logging;

using Moq;

using Xunit;

namespace EasySynQ.Tests.Unit.UI.Login;

public class LoginViewModelTests
{
    private static readonly DateTime FixedUtcNow =
        new(2026, 5, 12, 18, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IAuthenticationService> _auth = new(MockBehavior.Strict);
    private readonly Mock<IClock> _clock = new(MockBehavior.Strict);
    private readonly Mock<ILogger<LoginViewModel>> _logger = new();

    private LoginViewModel BuildSut()
    {
        _clock.Setup(c => c.UtcNow).Returns(FixedUtcNow);
        // The [LoggerMessage] source generator wraps every emit in
        // `if (_logger.IsEnabled(level))`. Moq's default for unstubbed bool
        // members is false, which would suppress the Log call we verify in
        // the system-error test. Stubbing IsEnabled true keeps the generator's
        // guard transparent.
        _logger.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        return new LoginViewModel(_auth.Object, _clock.Object, _logger.Object);
    }

    private static User NewUser(string username = "alice") =>
        new(
            id: Guid.NewGuid(),
            username: username,
            displayName: "Alice Example",
            passwordHash: "hash",
            passwordSalt: "salt",
            passwordIterationCount: 10_000,
            mustChangePassword: false);

    [Fact]
    public async Task SignInAsync_ValidCredentials_RaisesLoginSucceededWithUserAsync()
    {
        var sut = BuildSut();
        var user = NewUser();
        _auth.Setup(a => a.AuthenticateAsync("alice", "pw", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthenticationResult.Success(user, RequiresPasswordChange: false));

        AuthenticatedUserEventArgs? raised = null;
        var raisedCount = 0;
        sut.LoginSucceeded += (_, e) => { raised = e; raisedCount++; };

        sut.Username = "alice";
        await sut.SignInCommand.ExecuteAsync("pw");

        raisedCount.Should().Be(1);
        raised.Should().NotBeNull();
        raised!.User.Should().BeSameAs(user);
        raised.RequiresPasswordChange.Should().BeFalse();
        sut.ErrorMessage.Should().BeNull();
        sut.HasError.Should().BeFalse();
        sut.LogCorrelationId.Should().BeNull();
        sut.IsLockedOut.Should().BeFalse();
        sut.IsSigningIn.Should().BeFalse();
    }

    [Fact]
    public async Task SignInAsync_InvalidCredentials_SurfacesGenericErrorWithoutCorrelationIdAsync()
    {
        var sut = BuildSut();
        _auth.Setup(a => a.AuthenticateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthenticationResult.InvalidCredentials());

        var loginSucceededRaised = false;
        sut.LoginSucceeded += (_, _) => loginSucceededRaised = true;

        sut.Username = "alice";
        await sut.SignInCommand.ExecuteAsync("wrong");

        sut.ErrorMessage.Should().Be("Invalid username or password.");
        sut.HasError.Should().BeTrue();
        sut.LogCorrelationId.Should().BeNull();
        sut.IsLockedOut.Should().BeFalse();
        loginSucceededRaised.Should().BeFalse();
    }

    [Fact]
    public async Task SignInAsync_IdentityServiceThrows_SurfacesErrorWithCorrelationIdAndLogsAsync()
    {
        var sut = BuildSut();
        var boom = new InvalidOperationException("boom");
        _auth.Setup(a => a.AuthenticateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(boom);

        sut.Username = "alice";
        await sut.SignInCommand.ExecuteAsync("pw");

        sut.LogCorrelationId.Should().NotBeNullOrWhiteSpace();
        sut.LogCorrelationId!.Length.Should().Be(8);
        sut.LogCorrelationId.Should().MatchRegex("^[0-9A-F]{8}$");
        sut.ErrorMessage.Should().StartWith("Sign-in failed. Please contact support with reference ");
        sut.ErrorMessage.Should().EndWith(sut.LogCorrelationId + ".");
        sut.HasError.Should().BeTrue();

        // Capturing the correlation id once for the verify lambda — Moq's
        // lambda is invoked multiple times during expression evaluation and
        // re-reading sut.LogCorrelationId there would be racy with future
        // refactors that null it out early.
        var correlationId = sut.LogCorrelationId;
        _logger.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains(correlationId!, StringComparison.Ordinal)),
                It.Is<Exception?>(e => e == boom),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task SignInAsync_AccountLockedResult_SetsIsLockedOutAndPopulatesCountdownAsync()
    {
        var sut = BuildSut();
        var lockedUntil = FixedUtcNow.AddMinutes(14).AddSeconds(32);
        _auth.Setup(a => a.AuthenticateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthenticationResult.AccountLocked(lockedUntil));

        var loginSucceededRaised = false;
        sut.LoginSucceeded += (_, _) => loginSucceededRaised = true;

        sut.Username = "alice";
        await sut.SignInCommand.ExecuteAsync("pw");

        sut.IsLockedOut.Should().BeTrue();
        sut.LockoutCountdownDisplay.Should().Be("14m 32s");
        sut.ErrorMessage.Should().Be("Account locked. Try again in 14m 32s.");
        sut.LogCorrelationId.Should().BeNull();
        loginSucceededRaised.Should().BeFalse();

        // The lockout outcome must propagate into CanExecute — the submit
        // button must be disabled until IsLockedOut clears.
        sut.SignInCommand.CanExecute("pw").Should().BeFalse();
    }

    [Fact]
    public async Task SignInAsync_EmptyPassword_SurfacesValidationErrorAndDoesNotCallIdentityServiceAsync()
    {
        var sut = BuildSut();

        sut.Username = "alice";
        await sut.SignInCommand.ExecuteAsync(string.Empty);

        sut.ErrorMessage.Should().Be("Password is required.");
        sut.HasError.Should().BeTrue();
        sut.LogCorrelationId.Should().BeNull();
        sut.IsSigningIn.Should().BeFalse();
        _auth.Verify(
            a => a.AuthenticateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SignInAsync_AccountDisabledResult_SurfacesAdministratorContactErrorAndDoesNotRaiseLoginSucceededAsync()
    {
        var sut = BuildSut();
        _auth.Setup(a => a.AuthenticateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthenticationResult.AccountDisabled());

        var loginSucceededRaised = false;
        sut.LoginSucceeded += (_, _) => loginSucceededRaised = true;

        sut.Username = "alice";
        await sut.SignInCommand.ExecuteAsync("pw");

        sut.ErrorMessage.Should().Be("Account disabled. Contact your administrator.");
        sut.HasError.Should().BeTrue();
        sut.LogCorrelationId.Should().BeNull();
        // Disabled is admin-action, not time-based — IsLockedOut stays false
        // so the surface does not show a countdown that has no clock.
        sut.IsLockedOut.Should().BeFalse();
        loginSucceededRaised.Should().BeFalse();
    }

    [Fact]
    public void SignInCommand_EmptyOrWhitespaceUsername_CanExecuteReturnsFalse()
    {
        var sut = BuildSut();

        sut.Username = string.Empty;
        sut.SignInCommand.CanExecute("pw").Should().BeFalse();

        sut.Username = "   ";
        sut.SignInCommand.CanExecute("pw").Should().BeFalse();

        sut.Username = "alice";
        sut.SignInCommand.CanExecute("pw").Should().BeTrue();
    }

    [Fact]
    public void SignInCommand_IsLockedOut_CanExecuteReturnsFalse()
    {
        var sut = BuildSut();
        sut.Username = "alice";
        sut.SignInCommand.CanExecute("pw").Should().BeTrue();

        sut.IsLockedOut = true;
        sut.SignInCommand.CanExecute("pw").Should().BeFalse();

        sut.IsLockedOut = false;
        sut.SignInCommand.CanExecute("pw").Should().BeTrue();
    }
}
