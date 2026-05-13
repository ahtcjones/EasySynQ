using EasySynQ.Domain.Entities.Identity;

namespace EasySynQ.Services.Identity;

/// <summary>
/// Authenticates local users, manages lockout state, transparently
/// upgrades stored hashes when the policy iteration count rises, and
/// provides the first-run bootstrap and change-password flows. All
/// behaviors specified by ADR 0006.
/// </summary>
/// <remarks>
/// <para>
/// The service is the only writer of <see cref="User.LockedUntilUtc"/>,
/// <see cref="User.FailedLoginCount"/>, <see cref="User.LastLoginUtc"/>,
/// <see cref="User.PasswordHash"/>, <see cref="User.PasswordSalt"/>,
/// <see cref="User.PasswordIterationCount"/>, and
/// <see cref="User.MustChangePassword"/> in normal flows. (Admin-tool
/// flows and recovery flows can write the same fields through their own
/// well-audited paths.)
/// </para>
/// <para>
/// All persistence goes through <see cref="EasySynQ.Services.Abstractions.IUserRepository"/>
/// and <see cref="EasySynQ.Services.Abstractions.IUnitOfWork"/> — no direct
/// <c>DbContext</c> access from the service layer (per CLAUDE.md /
/// ADR 0002).
/// </para>
/// </remarks>
public interface IAuthenticationService
{
    /// <summary>
    /// Authenticates a username + password pair and updates lockout
    /// state accordingly.
    /// </summary>
    /// <param name="username">Supplied username. Whitespace-trimmed at
    /// the boundary; must not be null or empty after trim.</param>
    /// <param name="password">Supplied plaintext password. Must not be
    /// null or empty.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One of the <see cref="AuthenticationResult"/> variants
    /// — see that type's remarks for the discrimination rules.</returns>
    /// <exception cref="ArgumentException">Thrown when
    /// <paramref name="username"/> or <paramref name="password"/> is
    /// null/empty/whitespace.</exception>
    Task<AuthenticationResult> AuthenticateAsync(
        string username,
        string password,
        CancellationToken cancellationToken);

    /// <summary>
    /// Creates the first user in the system as an Administrator and
    /// returns it. Succeeds only when no users exist (per ADR 0006's
    /// first-run bootstrap design); throws if any user already exists.
    /// The created user has <see cref="User.MustChangePassword"/> =
    /// <see langword="false"/> — they just chose the password.
    /// </summary>
    /// <param name="username">Desired username for the Administrator.</param>
    /// <param name="password">Desired password. Must satisfy the policy's
    /// <see cref="IPasswordPolicy.MinimumLength"/>.</param>
    /// <param name="displayName">Desired display name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created and persisted <see cref="User"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when any input is
    /// null/empty/whitespace, or when <paramref name="password"/> is
    /// shorter than the policy's minimum length.</exception>
    /// <exception cref="InvalidOperationException">Thrown when at least
    /// one user already exists — the bootstrap window has closed.</exception>
    Task<User> CreateBootstrapAdministratorAsync(
        string username,
        string password,
        string displayName,
        CancellationToken cancellationToken);

    /// <summary>
    /// Changes a user's password. Requires the current password to
    /// match; on success, replaces the stored hash and clears
    /// <see cref="User.MustChangePassword"/>.
    /// </summary>
    /// <param name="userId">Identifier of the user whose password is
    /// being changed.</param>
    /// <param name="currentPassword">The user's current plaintext
    /// password — must match the stored hash.</param>
    /// <param name="newPassword">The new plaintext password — must
    /// satisfy the policy's
    /// <see cref="IPasswordPolicy.MinimumLength"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> on success;
    /// <see langword="false"/> when <paramref name="currentPassword"/>
    /// does not match the stored hash. (No state is modified on a
    /// false return.)</returns>
    /// <exception cref="ArgumentException">Thrown when any input is
    /// null/empty/whitespace, or when <paramref name="newPassword"/> is
    /// shorter than the policy's minimum length.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no user
    /// exists with <paramref name="userId"/>.</exception>
    Task<bool> ChangePasswordAsync(
        Guid userId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken);
}
