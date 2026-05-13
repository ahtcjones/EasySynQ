namespace EasySynQ.Services.Identity;

/// <summary>
/// Tunable parameters for password storage and online-attack throttling
/// per ADR 0006. Exposed as an interface so tests can substitute a
/// weakened policy (lower iteration count, shorter minimum length) for
/// throughput.
/// </summary>
/// <remarks>
/// <para>
/// Production deployments register a single
/// <see cref="PasswordPolicy"/> instance as a singleton. Test fixtures
/// register a <see cref="PasswordPolicy"/> with reduced parameters so
/// the suite stays fast — a single dedicated test exercises the
/// production iteration count.
/// </para>
/// </remarks>
public interface IPasswordPolicy
{
    /// <summary>
    /// Minimum length (in chars) of an acceptable plaintext password.
    /// Per ADR 0006, the production default is 12 and is the only
    /// password-strength rule.
    /// </summary>
    int MinimumLength { get; }

    /// <summary>
    /// PBKDF2 iteration count to apply when hashing newly-supplied
    /// passwords. Existing stored hashes carry their own iteration count
    /// (<c>User.PasswordIterationCount</c>) — this property is the
    /// target the auth service rehashes toward when it sees a stored
    /// hash below this threshold.
    /// </summary>
    int CurrentIterationCount { get; }

    /// <summary>
    /// Maximum consecutive failed login attempts before the account is
    /// locked out for <see cref="LockoutDuration"/>.
    /// </summary>
    int MaxFailedAttempts { get; }

    /// <summary>
    /// How long a locked-out account remains locked before the next
    /// authentication attempt is evaluated normally.
    /// </summary>
    TimeSpan LockoutDuration { get; }
}
