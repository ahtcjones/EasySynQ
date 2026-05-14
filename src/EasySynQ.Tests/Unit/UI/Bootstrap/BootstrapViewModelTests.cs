using AwesomeAssertions;

using EasySynQ.Domain.Entities.Identity;
using EasySynQ.Services.Bootstrap;
using EasySynQ.UI.Bootstrap;

using Microsoft.Extensions.Logging;

using Moq;

using Xunit;

namespace EasySynQ.Tests.Unit.UI.Bootstrap;

public class BootstrapViewModelTests
{
    private readonly Mock<IBootstrapService> _service = new(MockBehavior.Strict);
    private readonly Mock<ILogger<BootstrapViewModel>> _logger = new();

    private BootstrapViewModel BuildSut()
    {
        // Same IsEnabled rationale as LoginViewModelTests — the
        // [LoggerMessage] source generator wraps every emit in
        // `if (logger.IsEnabled(level))`. Moq's default for unstubbed
        // bool is false, which would silently suppress the Log call
        // the system-error test verifies.
        _logger.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        return new BootstrapViewModel(_service.Object, _logger.Object);
    }

    private static User NewUser(string username = "admin") =>
        new(
            id: Guid.NewGuid(),
            username: username,
            displayName: "Administrator",
            passwordHash: "hash",
            passwordSalt: "salt",
            passwordIterationCount: 10_000,
            mustChangePassword: false);

    [Fact]
    public async Task CreateAsync_HappyPath_PersistsUserAndRaisesBootstrapSucceededAsync()
    {
        var sut = BuildSut();
        var user = NewUser("admin");
        _service.Setup(s => s.CreateAdministratorAsync(
                "admin", "valid-password", "Administrator", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        BootstrapSucceededEventArgs? raised = null;
        var raisedCount = 0;
        sut.BootstrapSucceeded += (_, e) => { raised = e; raisedCount++; };
        var idempotencyRaised = false;
        sut.IdempotencyGuardFired += (_, _) => idempotencyRaised = true;

        sut.Username = "admin";
        sut.DisplayName = "Administrator";
        sut.SetPasswords("valid-password", "valid-password");
        await sut.CreateCommand.ExecuteAsync(null);

        raisedCount.Should().Be(1);
        raised.Should().NotBeNull();
        raised!.Administrator.Should().BeSameAs(user);
        idempotencyRaised.Should().BeFalse();
        sut.ErrorMessage.Should().BeNull();
        sut.HasError.Should().BeFalse();
        sut.LogCorrelationId.Should().BeNull();
        sut.IsCreating.Should().BeFalse();
    }

    [Fact]
    public async Task CreateAsync_PasswordMismatch_SetsErrorAndDoesNotCallServiceAsync()
    {
        var sut = BuildSut();

        sut.Username = "admin";
        sut.DisplayName = "Administrator";
        sut.SetPasswords("password-one", "password-two");
        await sut.CreateCommand.ExecuteAsync(null);

        sut.ErrorMessage.Should().Be("Passwords do not match.");
        sut.HasError.Should().BeTrue();
        sut.LogCorrelationId.Should().BeNull();
        // Strict mock — receiving any call would fail by default,
        // but VerifyNoOtherCalls makes the assertion explicit.
        _service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateAsync_PasswordTooShort_SurfacesValidationErrorWithoutCorrelationIdAsync()
    {
        var sut = BuildSut();
        _service.Setup(s => s.CreateAdministratorAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("Password must be at least 12 characters."));

        sut.Username = "admin";
        sut.DisplayName = "Administrator";
        sut.SetPasswords("short", "short");
        await sut.CreateCommand.ExecuteAsync(null);

        sut.ErrorMessage.Should().StartWith("Password does not meet requirements:");
        sut.ErrorMessage.Should().Contain("Password must be at least 12 characters.");
        sut.HasError.Should().BeTrue();
        sut.LogCorrelationId.Should().BeNull();
        // User-input validation, not a system error — no Error-level log.
        _logger.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateAsync_IdempotencyGuard_RaisesEventAndDoesNotSetErrorMessageAsync()
    {
        var sut = BuildSut();
        _service.Setup(s => s.CreateAdministratorAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(
                "Bootstrap is only available when no users exist; at least one user is already present."));

        var idempotencyRaisedCount = 0;
        sut.IdempotencyGuardFired += (_, _) => idempotencyRaisedCount++;
        var bootstrapRaised = false;
        sut.BootstrapSucceeded += (_, _) => bootstrapRaised = true;

        sut.Username = "admin";
        sut.DisplayName = "Administrator";
        sut.SetPasswords("valid-password", "valid-password");
        await sut.CreateCommand.ExecuteAsync(null);

        idempotencyRaisedCount.Should().Be(1);
        bootstrapRaised.Should().BeFalse();
        // Window is about to close — no point surfacing error text.
        sut.ErrorMessage.Should().BeNull();
        sut.HasError.Should().BeFalse();
        sut.LogCorrelationId.Should().BeNull();
    }

    [Fact]
    public async Task CreateAsync_SystemException_LogsAndSurfacesCorrelationIdAsync()
    {
        var sut = BuildSut();
        // Marker exception type unambiguously distinct from the
        // ArgumentException / InvalidOperationException paths handled
        // by their dedicated catch arms.
        var boom = new System.IO.InvalidDataException("disk on fire");
        _service.Setup(s => s.CreateAdministratorAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(boom);

        var bootstrapRaised = false;
        sut.BootstrapSucceeded += (_, _) => bootstrapRaised = true;
        var idempotencyRaised = false;
        sut.IdempotencyGuardFired += (_, _) => idempotencyRaised = true;

        sut.Username = "admin";
        sut.DisplayName = "Administrator";
        sut.SetPasswords("valid-password", "valid-password");
        await sut.CreateCommand.ExecuteAsync(null);

        sut.LogCorrelationId.Should().NotBeNullOrWhiteSpace();
        sut.LogCorrelationId!.Length.Should().Be(8);
        sut.LogCorrelationId.Should().MatchRegex("^[0-9A-F]{8}$");
        sut.ErrorMessage.Should().StartWith("Setup failed. Please contact support with reference ");
        sut.ErrorMessage.Should().EndWith(sut.LogCorrelationId + ".");
        sut.HasError.Should().BeTrue();
        bootstrapRaised.Should().BeFalse();
        idempotencyRaised.Should().BeFalse();

        // Capture for the verify lambda — Moq invokes the lambda
        // multiple times during expression evaluation and re-reading
        // sut.LogCorrelationId there would be racy with future
        // refactors that clear it.
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
    public void DisplayName_PreFillResumes_WhenUsernameIsClearedLast()
    {
        // Inverse clear-order regression of the lifecycle test below:
        // DisplayName cleared FIRST while Username still has content,
        // then Username cleared. The latch reset must fire in
        // OnUsernameChanged (not just OnDisplayNameChanged), otherwise
        // the latch stays flipped and pre-fill never resumes.
        var sut = BuildSut();

        // Phase 1 — pre-fill: DisplayName mirrors Username.
        sut.Username = "first";
        sut.DisplayName.Should().Be("first");

        // Phase 2 — manual edit latches; subsequent Username change
        // does NOT overwrite the custom DisplayName.
        sut.DisplayName = "Custom Name";
        sut.Username = "second";
        sut.DisplayName.Should().Be("Custom Name");

        // Phase 3 — clear DisplayName first. Username still has
        // content ("second"), so the latch stays flipped.
        sut.DisplayName = string.Empty;
        sut.DisplayName.Should().Be(string.Empty);

        // Phase 4 — clear Username second. DisplayName is already
        // empty, so the reset condition in OnUsernameChanged fires
        // and the latch resets. DisplayName remains empty (the
        // just-unlocked pre-fill assigns the empty Username value
        // back to DisplayName, a no-op since DisplayName == "").
        sut.Username = string.Empty;
        sut.DisplayName.Should().Be(string.Empty);

        // Phase 5 — pre-fill resumes on the next Username keystroke.
        // If this fails, the latch did NOT reset in Phase 4 and the
        // OnUsernameChanged reset branch is missing or broken.
        sut.Username = "third";
        sut.DisplayName.Should().Be("third");
    }

    [Fact]
    public void DisplayName_FollowsUsername_LatchesOnManualEdit_ResetsWhenBothCleared()
    {
        var sut = BuildSut();

        // Phase A — pre-fill: DisplayName mirrors Username.
        sut.Username = "jdoe";
        sut.DisplayName.Should().Be("jdoe");

        sut.Username = "jane.doe";
        sut.DisplayName.Should().Be("jane.doe");

        // Phase B — first manual edit: latch flips.
        sut.DisplayName = "Jane Doe";

        // Phase C — pre-fill suppressed: Username changes do not
        // overwrite the manually-chosen DisplayName.
        sut.Username = "different";
        sut.DisplayName.Should().Be("Jane Doe");

        // Phase D — user clears both fields to start over: latch
        // resets because the "DisplayName intentionally differs"
        // signal is gone.
        sut.Username = string.Empty;
        sut.DisplayName = string.Empty;

        // Phase E — pre-fill resumes on the next Username keystroke.
        sut.Username = "newuser";
        sut.DisplayName.Should().Be("newuser");
    }

    [Fact]
    public void CanCreate_RequiresAllFields()
    {
        var sut = BuildSut();

        // Empty everything.
        sut.CreateCommand.CanExecute(null).Should().BeFalse();

        // Username only (DisplayName auto-fills to the same value
        // via the pre-fill latch — also non-empty after this).
        sut.Username = "admin";
        sut.CreateCommand.CanExecute(null).Should().BeFalse();

        // Username + DisplayName, no passwords yet.
        sut.DisplayName = "Administrator";
        sut.CreateCommand.CanExecute(null).Should().BeFalse();

        // All four populated via SetPasswords.
        sut.SetPasswords("valid-password", "valid-password");
        sut.CreateCommand.CanExecute(null).Should().BeTrue();

        // IsCreating true (simulating an in-flight call) → false.
        sut.IsCreating = true;
        sut.CreateCommand.CanExecute(null).Should().BeFalse();

        sut.IsCreating = false;
        sut.CreateCommand.CanExecute(null).Should().BeTrue();
    }
}
