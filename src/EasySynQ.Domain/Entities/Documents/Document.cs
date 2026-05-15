using EasySynQ.Domain.Common;

namespace EasySynQ.Domain.Entities.Documents;

/// <summary>
/// A controlled internal document — the abstract identity that
/// <see cref="DocumentRevision"/> rows attach to (SPEC §5.1, ADR 0008).
/// One Document, many revisions; at most one revision is Active at any
/// instant.
/// </summary>
/// <remarks>
/// <para>
/// <b>Inheritance.</b> Document derives from <see cref="AuditableEntity"/>,
/// not <see cref="SignableEntity"/>. The Document row itself does not
/// carry a signature directly — the retirement signature attests to the
/// retirement *action* and is referenced via
/// <see cref="RetirementSignatureId"/>. Service-layer logic enforces the
/// "Document is locked" rules: retired Documents (where
/// <see cref="RetiredAtUtc"/> is non-null) accept no new revisions;
/// Documents whose any revision is past Draft state are
/// immutable-soft-delete-only per SPEC §3.5. Neither rule requires
/// <see cref="SignableEntity.LockedAtUtc"/> on the Document row itself.
/// </para>
/// <para>
/// <b>Internal library only.</b> External specifications (ASTM, AMS,
/// customer specs) are modeled by the separate
/// <see cref="ExternalDocument"/> entity. Document is internal-library
/// only; there is no <c>Library</c> discriminator.
/// </para>
/// <para>
/// <b>Number is org-assigned and unique within the deployment.</b> Format
/// is not prescribed (e.g., "SOP-Q-001", "WI-HT-042"); the uniqueness
/// constraint is enforced by a unique index in the data layer.
/// </para>
/// </remarks>
public class Document : AuditableEntity
{
    /// <summary>Unique entity identifier.</summary>
    public Guid Id { get; protected set; }

    /// <summary>
    /// Org-assigned document number, unique within the deployment.
    /// Format is org-specific (e.g., "SOP-Q-001").
    /// </summary>
    public string Number { get; protected set; } = string.Empty;

    /// <summary>Human-readable document title.</summary>
    public string Title { get; protected set; } = string.Empty;

    /// <summary>
    /// UTC instant at which the document was retired, or
    /// <see langword="null"/> if the document has not been retired.
    /// </summary>
    public DateTime? RetiredAtUtc { get; protected set; }

    /// <summary>
    /// Identifier of the user who retired the document, or
    /// <see langword="null"/> if the document has not been retired.
    /// Soft reference per ADR 0004 (no DB-level FK to Users).
    /// </summary>
    public Guid? RetiredByUserId { get; protected set; }

    /// <summary>
    /// Identifier of the Signature row attesting to the retirement, or
    /// <see langword="null"/> if the document has not been retired. Soft
    /// reference per ADR 0004 (no DB-level FK to Signatures).
    /// </summary>
    public Guid? RetirementSignatureId { get; protected set; }

    /// <summary>
    /// Parameterless constructor for the persistence layer. Do not call
    /// from application code.
    /// </summary>
    protected Document()
    {
    }

    /// <summary>
    /// Constructs a new internal Document. Newly-constructed Documents
    /// have no revisions; the first revision is created separately by
    /// the lifecycle service.
    /// </summary>
    /// <param name="id">Unique entity identifier. Must not be
    /// <see cref="Guid.Empty"/>.</param>
    /// <param name="number">Org-assigned document number. Must not be
    /// <see langword="null"/>, empty, or whitespace.</param>
    /// <param name="title">Human-readable document title. Must not be
    /// <see langword="null"/>, empty, or whitespace.</param>
    /// <exception cref="ArgumentException">Thrown when any input fails
    /// validation.</exception>
    public Document(Guid id, string number, string title)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Id must not be Guid.Empty.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(number);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        Id = id;
        Number = number;
        Title = title;
    }
}
