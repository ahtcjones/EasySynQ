using EasySynQ.Domain.Common;

namespace EasySynQ.Domain.Entities.Documents;

/// <summary>
/// Link between an internal <see cref="DocumentRevision"/> and an
/// <see cref="ExternalDocument"/> reference (SPEC §5.1, ADR 0008).
/// When the linked external is updated to a new revision (recorded
/// via <c>ExternalDocument.UpdateRevision</c>),
/// <see cref="CompatibilityReviewRequiredFlag"/> is set on every link
/// pointing at that external; it is cleared by a QM signoff or by
/// approving a new internal revision that supersedes the linked one.
/// </summary>
/// <remarks>
/// <para>
/// <b>Inheritance.</b> AuditableEntity — link metadata is audited but
/// not signed. The compatibility-flag clearance writes a Signature row
/// referencing the link (C8 scope); the Signature is the signed
/// evidence, the link is operational state.
/// </para>
/// <para>
/// <b>FK shape.</b> Both ends of the link are hard FKs within the
/// Document aggregate per ADR 0004: DocumentRevision and
/// ExternalDocument are both internal-to-the-aggregate entities.
/// </para>
/// </remarks>
public class DocumentLink : AuditableEntity
{
    /// <summary>Unique entity identifier.</summary>
    public Guid Id { get; protected set; }

    /// <summary>
    /// Identifier of the internal DocumentRevision side of the link.
    /// Hard FK (within-aggregate) per ADR 0004.
    /// </summary>
    public Guid DocumentRevisionId { get; protected set; }

    /// <summary>
    /// Identifier of the ExternalDocument side of the link. Hard FK
    /// (within-aggregate) per ADR 0004.
    /// </summary>
    public Guid ExternalDocumentId { get; protected set; }

    /// <summary>
    /// True when the external has been updated and this link's
    /// compatibility has not yet been re-validated. Cleared by QM
    /// signoff or by superseding the internal revision.
    /// </summary>
    public bool CompatibilityReviewRequiredFlag { get; protected set; }

    /// <summary>
    /// Parameterless constructor for the persistence layer. Do not call
    /// from application code.
    /// </summary>
    protected DocumentLink()
    {
    }

    /// <summary>
    /// Constructs a new DocumentLink. The compatibility flag defaults
    /// to <see langword="false"/>; the link is created in a known-good
    /// state and only flagged later if the external is updated.
    /// </summary>
    /// <param name="id">Unique entity identifier. Must not be
    /// <see cref="Guid.Empty"/>.</param>
    /// <param name="documentRevisionId">Internal DocumentRevision id.
    /// Must not be <see cref="Guid.Empty"/>.</param>
    /// <param name="externalDocumentId">ExternalDocument id. Must not be
    /// <see cref="Guid.Empty"/>.</param>
    /// <exception cref="ArgumentException">Thrown when any
    /// <see cref="Guid"/> argument is <see cref="Guid.Empty"/>.</exception>
    public DocumentLink(Guid id, Guid documentRevisionId, Guid externalDocumentId)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Id must not be Guid.Empty.", nameof(id));
        }
        if (documentRevisionId == Guid.Empty)
        {
            throw new ArgumentException(
                "DocumentRevisionId must not be Guid.Empty.",
                nameof(documentRevisionId));
        }
        if (externalDocumentId == Guid.Empty)
        {
            throw new ArgumentException(
                "ExternalDocumentId must not be Guid.Empty.",
                nameof(externalDocumentId));
        }

        Id = id;
        DocumentRevisionId = documentRevisionId;
        ExternalDocumentId = externalDocumentId;
        CompatibilityReviewRequiredFlag = false;
    }
}
