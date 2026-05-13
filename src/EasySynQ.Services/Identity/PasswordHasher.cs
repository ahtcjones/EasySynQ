using System.Security.Cryptography;

namespace EasySynQ.Services.Identity;

/// <summary>
/// Production <see cref="IPasswordHasher"/>. Computes PBKDF2-HMAC-SHA256
/// hashes via <see cref="Rfc2898DeriveBytes"/> and verifies them with
/// <see cref="CryptographicOperations.FixedTimeEquals(ReadOnlySpan{byte}, ReadOnlySpan{byte})"/>.
/// All parameter choices are pinned by ADR 0006.
/// </summary>
public sealed class PasswordHasher : IPasswordHasher
{
    /// <summary>16 bytes of salt — ADR 0006.</summary>
    public const int SaltByteLength = 16;

    /// <summary>32 bytes of hash output (full SHA-256) — ADR 0006.</summary>
    public const int HashByteLength = 32;

    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    private readonly IPasswordPolicy _policy;

    /// <summary>Constructs the hasher with the supplied policy.</summary>
    public PasswordHasher(IPasswordPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        _policy = policy;
    }

    /// <inheritdoc />
    public HashedPassword Hash(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        if (password.Length < _policy.MinimumLength)
        {
            throw new ArgumentException(
                $"Password must be at least {_policy.MinimumLength} characters.",
                nameof(password));
        }

        var iterations = _policy.CurrentIterationCount;
        var saltBytes = RandomNumberGenerator.GetBytes(SaltByteLength);
        var hashBytes = ComputeHash(password, saltBytes, iterations);

        return new HashedPassword(
            Hash: Convert.ToBase64String(hashBytes),
            Salt: Convert.ToBase64String(saltBytes),
            IterationCount: iterations);
    }

    /// <inheritdoc />
    public PasswordVerificationResult Verify(
        string password,
        string storedHash,
        string storedSalt,
        int storedIterationCount)
    {
        // Defensive guard: the auth service computes a dummy verify when
        // the user is not found (to mask username-existence timing). It
        // is the auth service's job to ensure the dummy inputs are
        // well-formed; if anything malformed reaches here, returning
        // Failure is the safe answer rather than throwing on the caller.
        if (string.IsNullOrEmpty(password)
            || string.IsNullOrEmpty(storedHash)
            || string.IsNullOrEmpty(storedSalt)
            || storedIterationCount <= 0)
        {
            return PasswordVerificationResult.Failure;
        }

        byte[] storedHashBytes;
        byte[] storedSaltBytes;
        try
        {
            storedHashBytes = Convert.FromBase64String(storedHash);
            storedSaltBytes = Convert.FromBase64String(storedSalt);
        }
        catch (FormatException)
        {
            return PasswordVerificationResult.Failure;
        }

        var computed = ComputeHash(password, storedSaltBytes, storedIterationCount);

        // CryptographicOperations.FixedTimeEquals — the load-bearing
        // detail of this verify path. Per ADR 0006: a == comparison on
        // byte arrays would short-circuit on the first byte mismatch
        // and leak hash structure through timing differences.
        // FixedTimeEquals runs in time independent of where the bytes
        // diverge.
        var matches = CryptographicOperations.FixedTimeEquals(computed, storedHashBytes);
        if (!matches)
        {
            return PasswordVerificationResult.Failure;
        }

        return storedIterationCount < _policy.CurrentIterationCount
            ? PasswordVerificationResult.SuccessRequiresRehash
            : PasswordVerificationResult.Success;
    }

    private static byte[] ComputeHash(string password, byte[] salt, int iterations)
    {
        // Rfc2898DeriveBytes.Pbkdf2 (static) is the BCL idiom for a one-
        // shot PBKDF2 computation — does not allocate the disposable
        // instance the older Rfc2898DeriveBytes(string,...) constructor
        // does, and accepts the algorithm explicitly.
        return Rfc2898DeriveBytes.Pbkdf2(
            password: password,
            salt: salt,
            iterations: iterations,
            hashAlgorithm: Algorithm,
            outputLength: HashByteLength);
    }
}
