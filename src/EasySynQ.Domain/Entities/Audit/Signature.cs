using EasySynQ.Domain.Common;

namespace EasySynQ.Domain.Entities.Audit;

/// <summary>
/// One digital-signature record per SPEC §3.4. Identity-anchored; never
/// an image of a signature. Captures who signed, when, in what role,
/// and which payload they signed (by SHA-256 hash).
/// </summary>
/// <remarks>
/// <para>
/// The signer's identity is captured via the inherited
/// <see cref="AuditableEntity.CreatedBy"/> property. The persistence
/// layer's standard-fields interceptor sets <c>CreatedBy</c> to the
/// authenticated user's identifier at the moment the row is written,
/// which by construction is the moment of signing. An earlier design
/// also carried a separate <c>UserId</c> field for "who signed" versus
/// "who inserted the row" — the distinction is theoretically defensible
/// but unused in practice. If a witnessed-signature concept ever
/// becomes real (an administrator recording a signature on behalf of
/// another user), it will be added then as a separate <c>WitnessedBy</c>
/// field, leaving <c>CreatedBy</c> as the signer's identity.
/// </para>
/// <para>
/// <see cref="Signature"/> inherits <see cref="AuditableEntity"/>
/// because signature records themselves are audit-logged. However, a
/// signature does not get re-signed; it is the act of signing.
/// </para>
/// </remarks>
public class Signature : AuditableEntity
{
    /// <summary>Unique entity identifier.</summary>
    public Guid Id { get; protected set; }

    /// <summary>UTC instant of the signature.</summary>
    public DateTime UtcTimestamp { get; protected set; }

    /// <summary>
    /// Role the user held at the moment of signing, captured as a string
    /// snapshot. Captured here rather than resolved from
    /// <see cref="EasySynQ.Domain.Entities.Identity.UserRole"/> at audit
    /// time because roles may change over time.
    /// </summary>
    public string RoleAtTimeOfSign { get; protected set; } = string.Empty;

    /// <summary>Canonical type name of the entity that was signed.</summary>
    public string SignedEntityType { get; protected set; } = string.Empty;

    /// <summary>
    /// Identifier of the entity that was signed, encoded as its
    /// canonical string form.
    /// </summary>
    public string SignedEntityId { get; protected set; } = string.Empty;

    /// <summary>
    /// SHA-256 hash of the canonical serialization of the signed
    /// payload, encoded as a 64-character lowercase hexadecimal string
    /// (see <see cref="HashFormat.IsValidSha256Hex(string?)"/>).
    /// </summary>
    public string PayloadHash { get; protected set; } = string.Empty;

    /// <summary>
    /// Parameterless constructor for the persistence layer. Do not call
    /// from application code.
    /// </summary>
    protected Signature()
    {
    }

    /// <summary>
    /// Constructs a new signature record. The signer's identity is
    /// populated into <see cref="AuditableEntity.CreatedBy"/> by the
    /// persistence interceptor at insert time, not by this constructor.
    /// </summary>
    /// <param name="id">Unique entity identifier. Must not be
    /// <see cref="Guid.Empty"/>.</param>
    /// <param name="utcTimestamp">UTC instant of the signature. Must be
    /// of <see cref="DateTimeKind.Utc"/>.</param>
    /// <param name="roleAtTimeOfSign">Role the user held at signing.
    /// Must not be <see langword="null"/>, empty, or whitespace.</param>
    /// <param name="signedEntityType">Canonical type name of the entity
    /// that was signed. Must not be <see langword="null"/>, empty, or
    /// whitespace.</param>
    /// <param name="signedEntityId">Canonical string form of the signed
    /// entity's identifier. Must not be <see langword="null"/>, empty,
    /// or whitespace.</param>
    /// <param name="payloadHash">SHA-256 hash of the signed payload,
    /// encoded as exactly 64 lowercase hexadecimal characters.</param>
    /// <exception cref="ArgumentException">Thrown when any input fails
    /// validation.</exception>
    public Signature(
        Guid id,
        DateTime utcTimestamp,
        string roleAtTimeOfSign,
        string signedEntityType,
        string signedEntityId,
        string payloadHash)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Id must not be Guid.Empty.", nameof(id));
        }
        if (utcTimestamp.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "UtcTimestamp must have DateTimeKind.Utc.",
                nameof(utcTimestamp));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(roleAtTimeOfSign);
        ArgumentException.ThrowIfNullOrWhiteSpace(signedEntityType);
        ArgumentException.ThrowIfNullOrWhiteSpace(signedEntityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadHash);

        if (!HashFormat.IsValidSha256Hex(payloadHash))
        {
            throw new ArgumentException(
                "PayloadHash must be exactly 64 lowercase hexadecimal characters.",
                nameof(payloadHash));
        }

        Id = id;
        UtcTimestamp = utcTimestamp;
        RoleAtTimeOfSign = roleAtTimeOfSign;
        SignedEntityType = signedEntityType;
        SignedEntityId = signedEntityId;
        PayloadHash = payloadHash;
    }
}
