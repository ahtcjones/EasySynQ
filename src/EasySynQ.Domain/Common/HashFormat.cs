namespace EasySynQ.Domain.Common;

/// <summary>
/// Validation helpers for the canonical hash formats used across the
/// domain.
/// </summary>
/// <remarks>
/// Centralising these checks keeps the format pinned in one place:
/// 64-character lowercase hexadecimal for SHA-256, matching the
/// encoding produced by <c>System.Convert.ToHexStringLower</c>. Every
/// hash field across the domain (signature payload hashes, snapshot
/// integrity hashes, vault blob identifiers) must use this same shape
/// so that cross-record comparisons are byte-for-byte stable.
/// </remarks>
public static class HashFormat
{
    /// <summary>
    /// Length in characters of a SHA-256 hash encoded as hexadecimal —
    /// 32 bytes × 2 chars per byte.
    /// </summary>
    public const int Sha256HexLength = 64;

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="value"/> is a
    /// valid SHA-256 hash encoded as exactly 64 lowercase hexadecimal
    /// characters (digits and <c>a</c>–<c>f</c>). Returns
    /// <see langword="false"/> for <see langword="null"/>, empty, wrong
    /// length, uppercase, mixed case, or any non-hex character.
    /// </summary>
    /// <param name="value">The candidate hash string.</param>
    /// <returns><see langword="true"/> if the value matches the
    /// required format.</returns>
    public static bool IsValidSha256Hex(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        if (value.Length != Sha256HexLength)
        {
            return false;
        }

        foreach (var c in value)
        {
            var isHexDigit = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
            if (!isHexDigit)
            {
                return false;
            }
        }

        return true;
    }
}
