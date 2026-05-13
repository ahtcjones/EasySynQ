namespace EasySynQ.Services.Identity;

/// <summary>
/// Hashes plaintext passwords for storage and verifies a supplied
/// plaintext against a stored hash. Implementation choice and parameters
/// are pinned by ADR 0006 (PBKDF2-HMAC-SHA256, salt + iteration count
/// stored per-user, constant-time comparison on verify).
/// </summary>
/// <remarks>
/// <para>
/// Both methods are pure: no I/O, no caller identity, no clock. The
/// auth service composes them with the user repository, the unit of
/// work, and the policy to produce the actual login flow.
/// </para>
/// </remarks>
public interface IPasswordHasher
{
    /// <summary>
    /// Hashes <paramref name="password"/> under the current policy's
    /// iteration count, generating a fresh random salt.
    /// </summary>
    /// <param name="password">Plaintext password. Must not be
    /// <see langword="null"/>, empty, or whitespace, and must satisfy
    /// the current policy's <see cref="IPasswordPolicy.MinimumLength"/>.</param>
    /// <returns>Base64-encoded hash, base64-encoded salt, and the
    /// iteration count used.</returns>
    /// <exception cref="ArgumentException">Thrown when
    /// <paramref name="password"/> is null/empty/whitespace or shorter
    /// than the policy's minimum length.</exception>
    HashedPassword Hash(string password);

    /// <summary>
    /// Verifies a plaintext password against stored hash material.
    /// </summary>
    /// <param name="password">Plaintext supplied by the caller.</param>
    /// <param name="storedHash">Base64-encoded stored hash from
    /// <see cref="EasySynQ.Domain.Entities.Identity.User.PasswordHash"/>.</param>
    /// <param name="storedSalt">Base64-encoded stored salt from
    /// <see cref="EasySynQ.Domain.Entities.Identity.User.PasswordSalt"/>.</param>
    /// <param name="storedIterationCount">Iteration count under which
    /// <paramref name="storedHash"/> was computed
    /// (<see cref="EasySynQ.Domain.Entities.Identity.User.PasswordIterationCount"/>).</param>
    /// <returns>
    /// <see cref="PasswordVerificationResult.Success"/> when the supplied
    /// password matches and <paramref name="storedIterationCount"/>
    /// equals the current policy;
    /// <see cref="PasswordVerificationResult.SuccessRequiresRehash"/>
    /// when the supplied password matches but the stored count is below
    /// the current policy;
    /// <see cref="PasswordVerificationResult.Failure"/> when the supplied
    /// password does not match or the inputs are malformed.
    /// </returns>
    PasswordVerificationResult Verify(
        string password,
        string storedHash,
        string storedSalt,
        int storedIterationCount);
}
