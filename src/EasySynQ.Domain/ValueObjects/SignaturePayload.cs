namespace EasySynQ.Domain.ValueObjects;

/// <summary>
/// Immutable record capturing one digital signature event, per SPEC §3.4.
/// </summary>
/// <remarks>
/// A signature is an identity-anchored record of who signed, when, in what
/// role, and which payload they signed (by SHA-256 hash). The payload hash
/// is stored as a 64-character lowercase hexadecimal string; see the
/// constructor's <c>payloadHash</c> parameter for the exact format
/// requirement.
/// </remarks>
public sealed record SignaturePayload
{
    /// <summary>Identifier of the user who signed.</summary>
    public string UserId { get; init; }

    /// <summary>UTC instant of signing.</summary>
    public DateTime UtcTimestamp { get; init; }

    /// <summary>
    /// Role the user held at the moment of signing. Captured here (rather
    /// than resolved from the user record at audit time) because roles may
    /// change over time and the signature must remain attributable to the
    /// role-at-time-of-sign.
    /// </summary>
    public string RoleAtTimeOfSign { get; init; }

    /// <summary>
    /// SHA-256 hash of the signed payload, encoded as a 64-character
    /// lowercase hexadecimal string (digits and <c>a</c>–<c>f</c>).
    /// </summary>
    public string PayloadHash { get; init; }

    /// <summary>
    /// Constructs a validated signature-payload record.
    /// </summary>
    /// <param name="userId">Identifier of the user who signed. Must not be
    /// <see langword="null"/>, empty, or whitespace.</param>
    /// <param name="utcTimestamp">UTC instant of signing. Must be of
    /// <see cref="DateTimeKind.Utc"/>.</param>
    /// <param name="roleAtTimeOfSign">Role the user held at signing. Must
    /// not be <see langword="null"/>, empty, or whitespace.</param>
    /// <param name="payloadHash">SHA-256 hash of the signed payload, encoded
    /// as exactly 64 lowercase hexadecimal characters.</param>
    /// <exception cref="ArgumentException">Thrown when any input fails
    /// validation.</exception>
    public SignaturePayload(
        string userId,
        DateTime utcTimestamp,
        string roleAtTimeOfSign,
        string payloadHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(roleAtTimeOfSign);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadHash);

        if (utcTimestamp.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "UtcTimestamp must have DateTimeKind.Utc.",
                nameof(utcTimestamp));
        }

        if (payloadHash.Length != 64 || !IsLowercaseHex(payloadHash))
        {
            throw new ArgumentException(
                "PayloadHash must be exactly 64 lowercase hexadecimal characters.",
                nameof(payloadHash));
        }

        UserId = userId;
        UtcTimestamp = utcTimestamp;
        RoleAtTimeOfSign = roleAtTimeOfSign;
        PayloadHash = payloadHash;
    }

    private static bool IsLowercaseHex(string s)
    {
        foreach (var c in s)
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
