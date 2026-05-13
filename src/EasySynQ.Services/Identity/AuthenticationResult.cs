using EasySynQ.Domain.Entities.Identity;

namespace EasySynQ.Services.Identity;

/// <summary>
/// Discriminated result of <see cref="IAuthenticationService.AuthenticateAsync"/>.
/// One of <see cref="Success"/>, <see cref="InvalidCredentials"/>,
/// <see cref="AccountLocked"/>, <see cref="AccountDisabled"/>, or
/// <see cref="FirstRunBootstrap"/>.
/// </summary>
/// <remarks>
/// <para>
/// Modeled as an abstract base with sealed nested types rather than as
/// a C# record-discriminator hack: callers pattern-match with
/// <c>switch (result)</c> and the compiler exhaustively enumerates the
/// branches. New result types in future will require new
/// <c>switch</c> arms — a desirable compile-time signal.
/// </para>
/// <para>
/// The unauthenticated cases (<see cref="InvalidCredentials"/>,
/// <see cref="FirstRunBootstrap"/>) deliberately carry no per-attempt
/// information beyond what the type itself encodes, so the auth surface
/// cannot be used as an oracle (does the user exist? is the password
/// just wrong? are we locked out for a different reason?).
/// </para>
/// </remarks>
public abstract record AuthenticationResult
{
    private AuthenticationResult() { }

    /// <summary>
    /// Authentication succeeded. <see cref="User"/> is the authenticated
    /// user; <see cref="RequiresPasswordChange"/> mirrors
    /// <c>User.MustChangePassword</c> at the moment of the success and
    /// is the cue for the UI to route to the change-password screen.
    /// </summary>
    public sealed record Success(User User, bool RequiresPasswordChange) : AuthenticationResult;

    /// <summary>
    /// Username unknown OR password wrong. The two cases are
    /// deliberately not distinguished — this is the masking that
    /// prevents the auth surface from being used as a username-existence
    /// oracle (ADR 0006).
    /// </summary>
    public sealed record InvalidCredentials : AuthenticationResult;

    /// <summary>
    /// Account is currently locked. <see cref="LockedUntilUtc"/> is the
    /// UTC instant the lockout expires. The auth service returns this
    /// regardless of whether the supplied password was correct, so the
    /// failure response cannot be used to probe for password correctness
    /// during a lockout.
    /// </summary>
    public sealed record AccountLocked(DateTime LockedUntilUtc) : AuthenticationResult;

    /// <summary>
    /// Account exists but has been administratively disabled
    /// (<c>User.IsDisabled = true</c>). Returned regardless of password
    /// correctness; the UI surfaces a "contact your administrator"
    /// message rather than a credential-failure prompt.
    /// </summary>
    public sealed record AccountDisabled : AuthenticationResult;

    /// <summary>
    /// No users exist in the system. The login screen should display the
    /// first-run bootstrap form and call
    /// <see cref="IAuthenticationService.CreateBootstrapAdministratorAsync"/>
    /// on submit.
    /// </summary>
    public sealed record FirstRunBootstrap : AuthenticationResult;
}
