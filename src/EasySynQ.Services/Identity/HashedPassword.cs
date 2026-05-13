namespace EasySynQ.Services.Identity;

/// <summary>
/// The output of <see cref="IPasswordHasher.Hash(string)"/>: a base64
/// PBKDF2 hash, the base64 salt that produced it, and the iteration
/// count under which it was computed.
/// </summary>
/// <param name="Hash">Base64-encoded PBKDF2 hash bytes (32 bytes raw,
/// per ADR 0006).</param>
/// <param name="Salt">Base64-encoded salt (16 bytes raw, per ADR 0006).</param>
/// <param name="IterationCount">PBKDF2 iteration count used to compute
/// <paramref name="Hash"/>.</param>
public sealed record HashedPassword(string Hash, string Salt, int IterationCount);
