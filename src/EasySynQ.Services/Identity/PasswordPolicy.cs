namespace EasySynQ.Services.Identity;

/// <summary>
/// Production <see cref="IPasswordPolicy"/> with the parameters pinned in
/// ADR 0006: PBKDF2-SHA256 600,000 iterations (OWASP 2023), 12-character
/// minimum password (NIST 800-63B length-over-complexity), 5 consecutive
/// failures within a 15-minute lockout.
/// </summary>
/// <remarks>
/// The default-constructed instance reflects ADR 0006's chosen values.
/// The constructor accepting parameters exists for tests, which
/// substitute weakened values (lower iteration count, shorter minimum)
/// to keep the suite fast — see <see cref="IPasswordPolicy"/> remarks.
/// </remarks>
public sealed class PasswordPolicy : IPasswordPolicy
{
    /// <summary>OWASP-recommended PBKDF2-SHA256 iteration count (2023).</summary>
    public const int DefaultIterationCount = 600_000;

    /// <summary>NIST 800-63B-aligned minimum length.</summary>
    public const int DefaultMinimumLength = 12;

    /// <summary>Default consecutive-failure threshold before lockout.</summary>
    public const int DefaultMaxFailedAttempts = 5;

    /// <summary>Default lockout window (15 minutes).</summary>
    public static readonly TimeSpan DefaultLockoutDuration = TimeSpan.FromMinutes(15);

    /// <inheritdoc />
    public int MinimumLength { get; }

    /// <inheritdoc />
    public int CurrentIterationCount { get; }

    /// <inheritdoc />
    public int MaxFailedAttempts { get; }

    /// <inheritdoc />
    public TimeSpan LockoutDuration { get; }

    /// <summary>
    /// Constructs the production policy with ADR 0006's default values.
    /// </summary>
    public PasswordPolicy()
        : this(
            DefaultMinimumLength,
            DefaultIterationCount,
            DefaultMaxFailedAttempts,
            DefaultLockoutDuration)
    {
    }

    /// <summary>
    /// Constructs a policy with explicit values. Tests use this overload
    /// to supply weakened parameters.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when any
    /// numeric parameter is non-positive or
    /// <paramref name="lockoutDuration"/> is non-positive.</exception>
    public PasswordPolicy(
        int minimumLength,
        int currentIterationCount,
        int maxFailedAttempts,
        TimeSpan lockoutDuration)
    {
        if (minimumLength <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumLength),
                minimumLength,
                "MinimumLength must be positive.");
        }
        if (currentIterationCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(currentIterationCount),
                currentIterationCount,
                "CurrentIterationCount must be positive.");
        }
        if (maxFailedAttempts <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxFailedAttempts),
                maxFailedAttempts,
                "MaxFailedAttempts must be positive.");
        }
        if (lockoutDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lockoutDuration),
                lockoutDuration,
                "LockoutDuration must be positive.");
        }

        MinimumLength = minimumLength;
        CurrentIterationCount = currentIterationCount;
        MaxFailedAttempts = maxFailedAttempts;
        LockoutDuration = lockoutDuration;
    }
}
