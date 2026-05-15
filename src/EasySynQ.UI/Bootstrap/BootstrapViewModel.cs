using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using EasySynQ.Services.Bootstrap;

using Microsoft.Extensions.Logging;

namespace EasySynQ.UI.Bootstrap;

/// <summary>
/// View model for the first-run bootstrap window. Mediates between
/// <see cref="IBootstrapService"/> and the bootstrap surface —
/// collects Username, DisplayName, and two passwords (held as
/// non-observable scratch state via
/// <see cref="SetPasswords(string, string)"/> so plaintext does not
/// flow through the binding system), and translates the service's
/// outcomes into the on-submit error region or the success /
/// idempotency events.
/// </summary>
/// <remarks>
/// <para>
/// <b>Password handling.</b> Mirrors <c>LoginViewModel</c> — neither
/// password is bound to a property. <c>PasswordBox.Password</c> is
/// unbindable by design (binding would surface plaintext via
/// property-change events); the view code-behind pushes both values
/// to the view model via <see cref="SetPasswords(string, string)"/>
/// on each <c>PasswordChanged</c>. The values are held as plain
/// string fields, not <c>[ObservableProperty]</c> surfaces, so they
/// do not appear in any change-notification path.
/// </para>
/// <para>
/// <b>DisplayName pre-fill.</b> While the user has not manually
/// edited DisplayName, it mirrors Username on each keystroke. The
/// first manual edit (DisplayName set to a value different from
/// Username) sets a one-way latch that stays set for the remainder
/// of the session — no further Username changes overwrite the
/// manually-chosen DisplayName.
/// </para>
/// <para>
/// <b>Validation timing.</b> Submit-only. Password-mismatch and the
/// <see cref="IPasswordHasher"/> length-policy violation surface as
/// <see cref="ErrorMessage"/> after Create is invoked; no
/// keystroke-by-keystroke or focus-loss validation. The view model
/// does NOT pre-validate password length —
/// <c>IPasswordHasher.Hash</c> throws
/// <see cref="ArgumentException"/> for too-short inputs and the
/// catch arm translates that to a user-friendly string.
/// </para>
/// <para>
/// <b>Audit emission.</b> The view model does not write audit log
/// rows. <see cref="IBootstrapService"/> writes the bootstrap audit
/// trail through the standard interceptor pipeline; see F1 Commit 1's
/// <c>BootstrapServiceTests</c> for the pinned four-row shape
/// (Role + User + UserRole + owned EffectiveDateRange) and the
/// Phase 1 Follow-Up on owned-type audit rows.
/// </para>
/// </remarks>
public partial class BootstrapViewModel : ObservableObject
{
    private readonly IBootstrapService _service;
    private readonly ILogger<BootstrapViewModel> _logger;

    private bool _displayNameManuallyEdited;
    private string _password = string.Empty;
    private string _confirmPassword = string.Empty;

    /// <summary>Constructs the view model.</summary>
    /// <param name="service">Bootstrap service.</param>
    /// <param name="logger">Logger. System-error correlation IDs are
    /// emitted at <see cref="LogLevel.Error"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when any
    /// argument is <see langword="null"/>.</exception>
    public BootstrapViewModel(
        IBootstrapService service,
        ILogger<BootstrapViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(logger);
        _service = service;
        _logger = logger;
    }

    /// <summary>Username entered by the user. Empty by default.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private string _username = string.Empty;

    /// <summary>
    /// Display name. Pre-fills from <see cref="Username"/> on each
    /// keystroke until the user manually edits it; thereafter
    /// independent for the rest of the session.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private string _displayName = string.Empty;

    /// <summary>
    /// User-facing error message, or <see langword="null"/> when the
    /// form has no error to display.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    /// <summary>
    /// Short correlation identifier surfaced alongside SYSTEM ERROR
    /// outcomes so support can match the user's reference to the
    /// structured log entry. <see langword="null"/> for
    /// password-mismatch and password-policy outcomes — those are
    /// user-input issues, not system errors, and exposing a
    /// correlation ID for them would invite a support-channel signal
    /// that something broke when nothing did.
    /// </summary>
    [ObservableProperty]
    private string? _logCorrelationId;

    /// <summary>
    /// <see langword="true"/> while the create call is in flight.
    /// Disables the submit button.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private bool _isCreating;

    /// <summary>
    /// True when <see cref="ErrorMessage"/> is non-empty. Derived
    /// rather than stored so the view binds a single boolean to
    /// error-visibility.
    /// </summary>
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>
    /// Raised after a successful bootstrap. The shell subscribes to
    /// populate the current-user accessor and transition to
    /// <c>MainWindow</c> — same shape as
    /// <c>LoginViewModel.LoginSucceeded</c>.
    /// </summary>
    public event EventHandler<BootstrapSucceededEventArgs>? BootstrapSucceeded;

    /// <summary>
    /// Raised when <see cref="IBootstrapService.CreateAdministratorAsync"/>
    /// throws <see cref="InvalidOperationException"/> — the
    /// idempotency guard fired because a user appeared between
    /// startup detection and the create call (vanishingly unlikely
    /// on a single-process desktop app, but the contract is
    /// preserved). The shell shows a "setup not required" dialog
    /// and calls <c>Application.Current.Shutdown(0)</c>; the next
    /// launch routes to <c>LoginWindow</c> because the user count
    /// is now non-zero.
    /// </summary>
    public event EventHandler? IdempotencyGuardFired;

    /// <summary>
    /// Pre-fill <see cref="DisplayName"/> from
    /// <paramref name="value"/> until the first manual edit. Partial
    /// method body paired with the
    /// <c>[ObservableProperty]</c>-generated
    /// <c>OnUsernameChanged</c> partial declaration.
    /// </summary>
    /// <remarks>
    /// The both-empty latch reset is checked here AND in
    /// <see cref="OnDisplayNameChanged"/> because the reset condition
    /// is a property of state (both fields empty) rather than of
    /// which field changed. Without the reset here, clearing
    /// <see cref="DisplayName"/> first and then
    /// <see cref="Username"/> would leave the latch flipped — only
    /// <see cref="OnDisplayNameChanged"/> would have seen the
    /// both-empty state, and by the time
    /// <see cref="OnUsernameChanged"/> fires for the second clear
    /// the latch is still true and pre-fill stays blocked. Reset
    /// must run BEFORE the pre-fill guard so the just-reset latch is
    /// observed on this same call.
    /// </remarks>
    partial void OnUsernameChanged(string value)
    {
        if (string.IsNullOrEmpty(value) && string.IsNullOrEmpty(DisplayName))
        {
            _displayNameManuallyEdited = false;
        }

        if (!_displayNameManuallyEdited)
        {
            DisplayName = value;
        }
    }

    /// <summary>
    /// Maintains the manual-edit latch that governs whether
    /// <see cref="OnUsernameChanged"/>'s pre-fill stays active.
    /// Three cases:
    /// <list type="bullet">
    ///   <item>Pre-fill echo (value == Username): no change. The
    ///   pre-fill code itself sets <c>DisplayName = Username</c>,
    ///   so its echo through this partial must not trip the latch.</item>
    ///   <item>User cleared both fields (value and Username both
    ///   empty): reset the latch. The latch represents "the user
    ///   intends DisplayName to differ from Username," and that
    ///   intent is gone when the user clears everything to start
    ///   over. Pre-fill resumes on the next Username keystroke.</item>
    ///   <item>Any other divergence (value != Username): set the
    ///   latch. The user typed something different from Username,
    ///   so future Username changes must not overwrite it.</item>
    /// </list>
    /// </summary>
    partial void OnDisplayNameChanged(string value)
    {
        if (string.IsNullOrEmpty(value) && string.IsNullOrEmpty(Username))
        {
            _displayNameManuallyEdited = false;
        }
        else if (value != Username)
        {
            _displayNameManuallyEdited = true;
        }
    }

    /// <summary>
    /// Pushes the current password and confirm-password values from
    /// the two <c>PasswordBox</c> controls into the view model's
    /// scratch buffer. Called by the view's <c>PasswordChanged</c>
    /// handler on each keystroke of either box. Triggers a
    /// CanExecute re-evaluation on <see cref="CreateCommand"/> so
    /// the submit button enables / disables in response to typing.
    /// </summary>
    /// <param name="password">Current text of the first PasswordBox.
    /// <see langword="null"/> coerced to empty string.</param>
    /// <param name="confirm">Current text of the second PasswordBox.
    /// <see langword="null"/> coerced to empty string.</param>
    public void SetPasswords(string password, string confirm)
    {
        _password = password ?? string.Empty;
        _confirmPassword = confirm ?? string.Empty;
        CreateCommand.NotifyCanExecuteChanged();
    }

    private bool CanCreate() =>
        !IsCreating
        && !string.IsNullOrWhiteSpace(Username)
        && !string.IsNullOrWhiteSpace(DisplayName)
        && !string.IsNullOrEmpty(_password)
        && !string.IsNullOrEmpty(_confirmPassword);

    /// <summary>
    /// Validates the two passwords match, then delegates to
    /// <see cref="IBootstrapService.CreateAdministratorAsync"/> and
    /// translates the outcomes:
    /// <list type="bullet">
    ///   <item>Success → raise
    ///   <see cref="BootstrapSucceeded"/>.</item>
    ///   <item><see cref="InvalidOperationException"/> (idempotency
    ///   guard) → raise <see cref="IdempotencyGuardFired"/>; no
    ///   error state on the VM because the window is about to
    ///   close.</item>
    ///   <item><see cref="ArgumentException"/>
    ///   (<c>IPasswordHasher</c> length-policy gate) → surface a
    ///   clean user-facing string in <see cref="ErrorMessage"/>;
    ///   no correlation ID and no log emit because this is input
    ///   validation, not a system error.</item>
    ///   <item>Any other exception → log at Error with a
    ///   correlation ID, surface the system-error message in
    ///   <see cref="ErrorMessage"/>.</item>
    /// </list>
    /// </summary>
    /// <param name="cancellationToken">Cancellation token supplied
    /// by the command infrastructure.</param>
    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task CreateAsync(CancellationToken cancellationToken)
    {
        if (_password != _confirmPassword)
        {
            ErrorMessage = "Passwords do not match.";
            LogCorrelationId = null;
            return;
        }

        ErrorMessage = null;
        LogCorrelationId = null;

        IsCreating = true;
        try
        {
            var result = await _service
                .CreateAdministratorAsync(Username, _password, DisplayName, cancellationToken)
                .ConfigureAwait(true);

            BootstrapSucceeded?.Invoke(
                this,
                new BootstrapSucceededEventArgs(
                    result.Administrator,
                    result.Roles,
                    result.Permissions,
                    result.RolePermissions));
        }
        catch (OperationCanceledException)
        {
            // Cancellation is not a system error — propagate up to
            // the command infrastructure without surfacing a
            // correlation id.
            throw;
        }
        catch (InvalidOperationException)
        {
            // Idempotency guard: a user appeared between startup
            // detection and this call. The handler will surface the
            // "setup not required" dialog and quit; no error state
            // on the VM because the window is closing.
            IdempotencyGuardFired?.Invoke(this, EventArgs.Empty);
        }
        catch (ArgumentException ex)
        {
            // IPasswordHasher throws this on too-short passwords.
            // User-input validation, not a system error — no
            // correlation ID and no log emit.
            ErrorMessage = "Password does not meet requirements: " + ex.Message;
        }
#pragma warning disable CA1031 // Do not catch general exception types — this is the SYSTEM ERROR catch-all by design.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            var correlationId = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
            LogBootstrapSystemError(ex, Username, correlationId);
            LogCorrelationId = correlationId;
            ErrorMessage = $"Setup failed. Please contact support with reference {correlationId}.";
        }
        finally
        {
            IsCreating = false;
        }
    }

    /// <summary>
    /// Source-generated emit for the system-error catch-all in
    /// <c>CreateAsync</c>. Instance partial (not static) so the
    /// source generator picks up the
    /// <see cref="ILogger{TCategoryName}"/> field on this type —
    /// matches <c>LoginViewModel.LogSignInSystemError</c>'s pattern
    /// (VM convention is instance; App convention is static; the
    /// two do not bridge).
    /// </summary>
    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Error,
        Message = "Bootstrap failed for {Username} with correlation {CorrelationId}")]
    private partial void LogBootstrapSystemError(
        Exception exception,
        string username,
        string correlationId);
}
