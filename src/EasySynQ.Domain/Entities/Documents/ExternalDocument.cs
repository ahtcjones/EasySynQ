using EasySynQ.Domain.Common;

namespace EasySynQ.Domain.Entities.Documents;

/// <summary>
/// Reference to an externally-issued specification (ASTM, AMS, customer
/// spec, etc.) — SPEC §5.1 External Library (ADR 0008). EasySynQ tracks
/// metadata only; the external document's content is not stored or
/// version-controlled by this system. Internal documents can link to
/// an ExternalDocument via <see cref="DocumentLink"/>; when the external
/// is updated, the linked internal documents are flagged for
/// compatibility review.
/// </summary>
/// <remarks>
/// <para>
/// <b>Inheritance.</b> ExternalDocument derives from
/// <see cref="AuditableEntity"/> — it is audit-logged metadata but
/// never signed. The signing semantics for "this external version is
/// compatible with our internal document" attach to the
/// <see cref="DocumentLink"/>'s compatibility-flag clearance, not to
/// the ExternalDocument itself.
/// </para>
/// <para>
/// <b>No vault blob.</b> External document files live outside
/// EasySynQ. Future deployments may choose to attach reference copies
/// to internal DocumentRevisions (which carry their own
/// <c>VaultBlobId</c>), but the ExternalDocument entity holds metadata
/// only.
/// </para>
/// </remarks>
public class ExternalDocument : AuditableEntity
{
    /// <summary>Unique entity identifier.</summary>
    public Guid Id { get; protected set; }

    /// <summary>
    /// Issuing body (e.g., "ASTM", "AMS", "ISO", "{customer name}").
    /// </summary>
    public string IssuingBody { get; protected set; } = string.Empty;

    /// <summary>
    /// External designation (e.g., "ASTM A29", "AMS 2750G",
    /// "{customer}-{spec-id}").
    /// </summary>
    public string Designation { get; protected set; } = string.Empty;

    /// <summary>
    /// Current known revision label as published by the issuing body
    /// (e.g., "G", "2024-01").
    /// </summary>
    public string CurrentRevisionLabel { get; protected set; } = string.Empty;

    /// <summary>
    /// Effective date of the current revision, as published, or
    /// <see langword="null"/> if unknown or not applicable.
    /// </summary>
    public DateTime? CurrentEffectiveDateUtc { get; protected set; }

    /// <summary>
    /// Parameterless constructor for the persistence layer. Do not call
    /// from application code.
    /// </summary>
    protected ExternalDocument()
    {
    }

    /// <summary>
    /// Constructs a new ExternalDocument metadata record.
    /// </summary>
    /// <param name="id">Unique entity identifier. Must not be
    /// <see cref="Guid.Empty"/>.</param>
    /// <param name="issuingBody">Issuing body. Must not be
    /// <see langword="null"/>, empty, or whitespace.</param>
    /// <param name="designation">External designation. Must not be
    /// <see langword="null"/>, empty, or whitespace.</param>
    /// <param name="currentRevisionLabel">Current revision label. Must
    /// not be <see langword="null"/>, empty, or whitespace.</param>
    /// <param name="currentEffectiveDateUtc">Effective date of the
    /// current revision, or <see langword="null"/>. When non-null, must
    /// have <see cref="DateTimeKind.Utc"/>.</param>
    /// <exception cref="ArgumentException">Thrown when any input fails
    /// validation.</exception>
    public ExternalDocument(
        Guid id,
        string issuingBody,
        string designation,
        string currentRevisionLabel,
        DateTime? currentEffectiveDateUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Id must not be Guid.Empty.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(issuingBody);
        ArgumentException.ThrowIfNullOrWhiteSpace(designation);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentRevisionLabel);

        if (currentEffectiveDateUtc.HasValue
            && currentEffectiveDateUtc.Value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "CurrentEffectiveDateUtc must have DateTimeKind.Utc.",
                nameof(currentEffectiveDateUtc));
        }

        Id = id;
        IssuingBody = issuingBody;
        Designation = designation;
        CurrentRevisionLabel = currentRevisionLabel;
        CurrentEffectiveDateUtc = currentEffectiveDateUtc;
    }
}
