using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using EasySynQ.Services.Identity;
using EasySynQ.Services.Time;

using Microsoft.Extensions.Logging;

namespace EasySynQ.UI.Login;

/// <summary>
/// View model for the login window. Mediates between the authentication
/// service and the login surface — collects the username, accepts the
/// password as a command parameter (never bound, never stored), and
/// translates the auth service's discriminated result into the four
/// user-facing outcomes the surface needs to render
/// (<see cref="AuthenticationResult.FirstRunBootstrap"/> is unreachable
/// post-F1 Commit 2: <see cref="App"/> calls
/// <see cref="EasySynQ.Services.Bootstrap.IBootstrapService.IsBootstrapRequiredAsync"/>
/// at startup and routes to the bootstrap surface before LoginWindow is
/// ever resolved; the switch's <c>default</c> arm catches the variant
/// loudly if the path somehow fires).
/// </summary>
/// <remarks>
/// <para>
/// <b>Password handling.</b> The view model deliberately holds no
/// password property. WPF's <c>PasswordBox.Password</c> exposes a
/// non-bindable plaintext string by design (so view-model bindings do not
/// carry the password through the data-binding plumbing); the view passes
/// the value to the command as a parameter at submit time only.
/// </para>
/// <para>
/// <b>Lockout policy.</b> The view model does not implement lockout
/// policy — threshold, window length, and counter mechanics live in
/// <see cref="IAuthenticationService"/> per ADR 0006. The view model only
/// reacts to the <see cref="AuthenticationResult.AccountLocked"/> outcome
/// and formats the remaining lockout window for display.
/// </para>
/// <para>
/// <b>Audit emission.</b> The view model does not write audit-log rows.
/// The current audit-log shape (SPEC §3.4 / ADR 0002) emits one row per
/// entity Insert/Update/Delete/HardDelete via the
/// <c>AuditSaveChangesInterceptor</c>; sign-in events that do not produce
/// an entity mutation (unknown-user invalid credentials, already-locked
/// retries, disabled-account attempts, first-run bootstrap) currently
/// gain no audit trail. Tracked in <c>docs/SESSION_NOTES.md</c> Phase 1
/// Follow-Ups.
/// </para>
/// </remarks>
public partial class LoginViewModel : ObservableObject
{
    private readonly IAuthenticationService _authentication;
    private readonly IClock _clock;
    private readonly ILogger<LoginViewModel> _logger;

    /// <summary>
    /// Constructs the view model.
    /// </summary>
    /// <param name="authentication">Authentication service.</param>
    /// <param name="clock">Clock used to compute the remaining lockout
    /// countdown from the auth service's reported lockout-end instant.</param>
    /// <param name="logger">Logger. System-error correlation IDs are
    /// emitted at <see cref="LogLevel.Error"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument
    /// is <see langword="null"/>.</exception>
    public LoginViewModel(
        IAuthenticationService authentication,
        IClock clock,
        ILogger<LoginViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(authentication);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        _authentication = authentication;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Username entered by the user. Empty by default.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    private string _username = string.Empty;

    /// <summary>
    /// User-facing error message, or <see langword="null"/> when the form
    /// has no error to display.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    /// <summary>
    /// Short correlation identifier surfaced alongside SYSTEM ERROR
    /// outcomes so support can match the user's reference to the
    /// structured log entry produced at the time of the failure. Always
    /// <see langword="null"/> for credential / lockout / disabled /
    /// bootstrap outcomes — those are not system errors and exposing a
    /// correlation ID for them would invite a support-channel signal that
    /// nothing went wrong.
    /// </summary>
    [ObservableProperty]
    private string? _logCorrelationId;

    /// <summary>
    /// <see langword="true"/> when the auth service has reported that the
    /// account is locked for time-based reasons (failed-attempt threshold
    /// crossed; ADR 0006). Disabling the submit button is the principal
    /// effect — see <see cref="CanSignIn"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    private bool _isLockedOut;

    /// <summary>
    /// Human-readable countdown to the end of the current lockout, e.g.
    /// <c>"14m 32s"</c>. Computed once at the moment the auth service
    /// returns <see cref="AuthenticationResult.AccountLocked"/> using
    /// <see cref="IClock.UtcNow"/> as the reference instant; the view
    /// model intentionally does not run a ticker — the view is free to
    /// animate down from this value if it wants live feedback.
    /// </summary>
    [ObservableProperty]
    private string? _lockoutCountdownDisplay;

    /// <summary>
    /// <see langword="true"/> while the auth service call is in flight.
    /// Disables the submit button.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    private bool _isSigningIn;

    /// <summary>
    /// True when <see cref="ErrorMessage"/> is non-empty. Derived rather
    /// than stored so the view can bind a single boolean to error-visibility.
    /// </summary>
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>
    /// Raised after a <see cref="AuthenticationResult.Success"/> outcome.
    /// The shell subscribes to navigate to the post-login destination
    /// (main window or change-password dialog).
    /// </summary>
    public event EventHandler<AuthenticatedUserEventArgs>? LoginSucceeded;

    /// <summary>
    /// Indicates whether the submit command can run for the current view
    /// model state. The password is not observed here — its presence is
    /// validated inside the command body so the password text never has
    /// to live anywhere observable to the binding system.
    /// </summary>
    private bool CanSignIn(string? password) =>
        !IsSigningIn && !IsLockedOut && !string.IsNullOrWhiteSpace(Username);

    /// <summary>
    /// Attempts to authenticate <see cref="Username"/> with the supplied
    /// password. Translates the auth service's
    /// <see cref="AuthenticationResult"/> into the five view-model
    /// outcomes — success, invalid credentials, account locked, account
    /// disabled, first-run bootstrap — plus a sixth catch-all for
    /// system-error exceptions thrown by the service.
    /// </summary>
    /// <param name="password">Password entered in the
    /// <c>PasswordBox</c>. Passed as a command parameter to keep it out
    /// of the view-model state graph.</param>
    /// <param name="cancellationToken">Cancellation token supplied by
    /// the command infrastructure.</param>
    [RelayCommand(CanExecute = nameof(CanSignIn))]
    private async Task SignInAsync(string? password, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(password))
        {
            ErrorMessage = "Password is required.";
            LogCorrelationId = null;
            return;
        }

        IsSigningIn = true;
        try
        {
            var result = await _authentication
                .AuthenticateAsync(Username, password, cancellationToken)
                .ConfigureAwait(true);

            switch (result)
            {
                case AuthenticationResult.Success success:
                    ErrorMessage = null;
                    LogCorrelationId = null;
                    IsLockedOut = false;
                    LockoutCountdownDisplay = null;
                    LoginSucceeded?.Invoke(
                        this,
                        new AuthenticatedUserEventArgs(
                            success.User,
                            success.RequiresPasswordChange,
                            success.Roles,
                            success.Permissions));
                    break;

                case AuthenticationResult.InvalidCredentials:
                    // Deliberately vague — the auth service masks the
                    // unknown-user / wrong-password distinction (ADR 0006);
                    // the view model must not undo that masking.
                    ErrorMessage = "Invalid username or password.";
                    LogCorrelationId = null;
                    break;

                case AuthenticationResult.AccountLocked locked:
                    IsLockedOut = true;
                    LockoutCountdownDisplay = FormatLockoutCountdown(locked.LockedUntilUtc - _clock.UtcNow);
                    ErrorMessage = $"Account locked. Try again in {LockoutCountdownDisplay}.";
                    LogCorrelationId = null;
                    break;

                case AuthenticationResult.AccountDisabled:
                    // Distinct from a time-based lockout — admin action is
                    // required, not patience. IsLockedOut stays false on
                    // purpose so the surface does not show a countdown.
                    ErrorMessage = "Account disabled. Contact your administrator.";
                    LogCorrelationId = null;
                    break;

                default:
                    // Defensive: a new AuthenticationResult variant added
                    // without a matching arm here is a service-contract
                    // change the view model must explicitly handle. Also
                    // catches FirstRunBootstrap, which is unreachable
                    // post-F1 Commit 2 (App routes to BootstrapWindow at
                    // startup before LoginWindow resolves) — loud failure
                    // here beats silently dead-ending if that invariant
                    // ever breaks.
                    throw new InvalidOperationException(
                        $"Unhandled authentication result: {result.GetType().Name}");
            }
        }
        catch (OperationCanceledException)
        {
            // Cancellation is not a system error — propagate up to the
            // command infrastructure without surfacing a correlation id.
            throw;
        }
#pragma warning disable CA1031 // Do not catch general exception types — this is the SYSTEM ERROR catch-all by design.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            var correlationId = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
            LogSignInSystemError(ex, Username, correlationId);
            LogCorrelationId = correlationId;
            ErrorMessage = $"Sign-in failed. Please contact support with reference {correlationId}.";
        }
        finally
        {
            IsSigningIn = false;
        }
    }

    /// <summary>
    /// Source-generated <see cref="LogLevel.Error"/> log emitter for the
    /// system-error catch-all. Uses the
    /// <see cref="LoggerMessageAttribute"/> pattern (CA1848 / .NET 6+
    /// recommendation) so the structured message template is compiled
    /// once rather than re-parsed at every call site. The generator
    /// discovers the <see cref="ILogger{TCategoryName}"/> field on this
    /// type and emits the call against it; no <c>ILogger</c> parameter
    /// is required.
    /// </summary>
    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Error,
        Message = "Sign-in failed for {Username} with correlation {CorrelationId}")]
    private partial void LogSignInSystemError(
        Exception exception,
        string username,
        string correlationId);

    /// <summary>
    /// Formats a remaining lockout window as <c>"Xm Ys"</c>. Negative
    /// or zero spans clamp to <c>"0m 0s"</c> — the auth service can
    /// (very narrowly) return a <see cref="AuthenticationResult.AccountLocked"/>
    /// whose lockout instant is in the past relative to
    /// <see cref="IClock.UtcNow"/> if a race or clock-skew condition
    /// lines up, and "0m 0s" is more informative than a negative duration.
    /// </summary>
    private static string FormatLockoutCountdown(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero)
        {
            return "0m 0s";
        }
        var totalMinutes = (int)remaining.TotalMinutes;
        var seconds = remaining.Seconds;
        return $"{totalMinutes}m {seconds}s";
    }
}
