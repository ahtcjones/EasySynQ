using EasySynQ.Domain.Common;

namespace EasySynQ.Domain.Entities.Identity;

/// <summary>
/// Local user account for authentication and audit attribution. Local
/// users are owned by EasySynQ; the system never delegates identity to a
/// third party (ADR 0001 — no external identity providers).
/// </summary>
/// <remarks>
/// <para>
/// The entity stores hashed-and-salted password material only. There is
/// no property anywhere on this type — public, protected, or private —
/// that holds the plaintext password. The auth service computes the
/// PBKDF2 hash from the plaintext at the boundary and writes only the
/// hash, salt, and iteration count to the entity.
/// </para>
/// <para>
/// The hash/salt encoding and PBKDF2 parameter choices are pinned in
/// ADR 0004 (to be authored before Phase 1 ships). This entity treats
/// them as opaque strings.
/// </para>
/// </remarks>
public class User : AuditableEntity
{
    /// <summary>Unique entity identifier.</summary>
    public Guid Id { get; protected set; }

    /// <summary>
    /// Login username. Uniqueness is enforced at the data layer (unique
    /// index on this column); the domain treats it as an arbitrary string.
    /// </summary>
    public string Username { get; protected set; } = string.Empty;

    /// <summary>
    /// Display name for UI rendering (for example, <c>"M. Rodriguez"</c>).
    /// </summary>
    public string DisplayName { get; protected set; } = string.Empty;

    /// <summary>
    /// PBKDF2 hash of the user's password, encoded per ADR 0004. Opaque
    /// to the domain.
    /// </summary>
    public string PasswordHash { get; protected set; } = string.Empty;

    /// <summary>
    /// PBKDF2 salt used in computing <see cref="PasswordHash"/>, encoded
    /// per ADR 0004.
    /// </summary>
    public string PasswordSalt { get; protected set; } = string.Empty;

    /// <summary>
    /// PBKDF2 iteration count used in computing <see cref="PasswordHash"/>.
    /// Stored per-user because the policy iteration count may increase
    /// over time as compute hardware improves; old hashes remain
    /// verifiable against the count that produced them.
    /// </summary>
    public int PasswordIterationCount { get; protected set; }

    /// <summary>
    /// True when the user is required to change their password on next
    /// login. Set when an admin provisions or resets the account.
    /// </summary>
    public bool MustChangePassword { get; protected set; }

    /// <summary>
    /// UTC instant of the user's most recent successful login, or
    /// <see langword="null"/> if the user has never logged in.
    /// </summary>
    public DateTime? LastLoginUtc { get; protected set; }

    /// <summary>
    /// True when the account is administratively disabled. Disabled
    /// accounts cannot authenticate but remain visible for audit
    /// attribution of historical activity.
    /// </summary>
    public bool IsDisabled { get; protected set; }

    /// <summary>
    /// Parameterless constructor for the persistence layer. Do not call
    /// from application code.
    /// </summary>
    protected User()
    {
    }

    /// <summary>
    /// Constructs a new user with the supplied identity and password
    /// material. Initial state is <see cref="LastLoginUtc"/> =
    /// <see langword="null"/> and <see cref="IsDisabled"/> =
    /// <see langword="false"/>.
    /// </summary>
    /// <param name="id">Unique entity identifier. Must not be
    /// <see cref="Guid.Empty"/>.</param>
    /// <param name="username">Login username. Must not be
    /// <see langword="null"/>, empty, or whitespace.</param>
    /// <param name="displayName">Display name for UI rendering. Must
    /// not be <see langword="null"/>, empty, or whitespace.</param>
    /// <param name="passwordHash">PBKDF2 hash of the user's password,
    /// encoded per ADR 0004. Must not be <see langword="null"/>, empty,
    /// or whitespace.</param>
    /// <param name="passwordSalt">PBKDF2 salt used in computing
    /// <paramref name="passwordHash"/>, encoded per ADR 0004. Must not
    /// be <see langword="null"/>, empty, or whitespace.</param>
    /// <param name="passwordIterationCount">PBKDF2 iteration count used
    /// in computing <paramref name="passwordHash"/>. Must be positive.</param>
    /// <param name="mustChangePassword">Whether the user must change
    /// their password on next login.</param>
    /// <exception cref="ArgumentException">Thrown when any string input
    /// or <paramref name="id"/> fails validation.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when
    /// <paramref name="passwordIterationCount"/> is not positive.</exception>
    public User(
        Guid id,
        string username,
        string displayName,
        string passwordHash,
        string passwordSalt,
        int passwordIterationCount,
        bool mustChangePassword)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Id must not be Guid.Empty.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordSalt);

        if (passwordIterationCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(passwordIterationCount),
                passwordIterationCount,
                "PasswordIterationCount must be positive.");
        }

        Id = id;
        Username = username;
        DisplayName = displayName;
        PasswordHash = passwordHash;
        PasswordSalt = passwordSalt;
        PasswordIterationCount = passwordIterationCount;
        MustChangePassword = mustChangePassword;
    }
}
