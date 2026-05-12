using EasySynQ.Domain.Enums;

namespace EasySynQ.Domain.Entities.Audit;

/// <summary>
/// One row in the global audit log, capturing a single insert, update,
/// soft-delete, or hard-delete on a compliance-critical entity per
/// SPEC §3.4 and ADR 0002.
/// </summary>
/// <remarks>
/// <para>
/// The audit log is append-only — there is no domain method to mutate
/// an existing entry. <see cref="AuditLogEntry"/> deliberately does not
/// inherit from <see cref="Common.AuditableEntity"/>: audit-of-audit-log
/// is unnecessary because the table itself is the trail.
/// </para>
/// <para>
/// <see cref="EntityId"/> is intentionally typed as <see cref="string"/>
/// rather than a strongly-typed identifier (Guid, or a dedicated
/// <c>EntityRef(type, id)</c> value object). The tradeoff: a single
/// audit-log table must accept identifiers from every
/// compliance-critical entity — Guid for <c>User</c>, formatted strings
/// like <c>"J-2026-0847"</c> for <c>Job</c>, etc. — and the textual
/// form is the lowest common denominator. Strong-typing would force
/// either an <c>EntityRef</c> abstraction or per-entity audit tables;
/// neither solves the underlying parse-when-reading problem and both
/// add complexity disproportionate to current needs. Revisit if a real
/// cross-entity traversal use case emerges (for example, "give me all
/// audit entries for any entity owned by user X").
/// </para>
/// <para>
/// <see cref="Before"/> and <see cref="After"/> shape is determined by
/// <see cref="Action"/>:
/// <list type="bullet">
///   <item><see cref="AuditAction.Insert"/> — Before is
///   <see langword="null"/>; After is required.</item>
///   <item><see cref="AuditAction.Update"/> — both required.</item>
///   <item><see cref="AuditAction.Delete"/> (soft) — both required (the
///   transition to <c>IsDeleted = true</c> is itself state).</item>
///   <item><see cref="AuditAction.HardDelete"/> — Before is required;
///   After is <see langword="null"/> per ADR 0002.</item>
/// </list>
/// The constructor enforces these shape rules.
/// </para>
/// </remarks>
public class AuditLogEntry
{
    /// <summary>Unique entry identifier.</summary>
    public Guid Id { get; protected set; }

    /// <summary>UTC instant the operation was recorded.</summary>
    public DateTime UtcTimestamp { get; protected set; }

    /// <summary>
    /// Identifier of the user who performed the operation, or
    /// <see langword="null"/> for system-generated entries (for example,
    /// snapshot creation, integrity verification, schema migration).
    /// </summary>
    public Guid? UserId { get; protected set; }

    /// <summary>
    /// Canonical type name of the operational entity affected (for
    /// example, <c>"User"</c>, <c>"Job"</c>, <c>"NCR"</c>).
    /// </summary>
    public string EntityTypeName { get; protected set; } = string.Empty;

    /// <summary>
    /// Identifier of the operational entity affected, encoded as its
    /// canonical string form.
    /// </summary>
    public string EntityId { get; protected set; } = string.Empty;

    /// <summary>The action that produced this entry.</summary>
    public AuditAction Action { get; protected set; }

    /// <summary>
    /// JSON snapshot of the entity's state before the operation, or
    /// <see langword="null"/> for <see cref="AuditAction.Insert"/>.
    /// </summary>
    public string? Before { get; protected set; }

    /// <summary>
    /// JSON snapshot of the entity's state after the operation, or
    /// <see langword="null"/> for <see cref="AuditAction.HardDelete"/>.
    /// </summary>
    public string? After { get; protected set; }

    /// <summary>
    /// Correlation identifier shared by all audit-log entries that arose
    /// from a single user-facing operation. Allows a multi-row operation
    /// (for example, approving a document revision, which writes both
    /// the revision update and the retraining-required cascade) to be
    /// reconstructed as a single business event.
    /// </summary>
    public Guid CorrelationId { get; protected set; }

    /// <summary>
    /// Parameterless constructor for the persistence layer. Do not call
    /// from application code.
    /// </summary>
    protected AuditLogEntry()
    {
    }

    /// <summary>
    /// Constructs a new audit-log entry. Validation enforces that
    /// <paramref name="before"/> / <paramref name="after"/> shape
    /// matches <paramref name="action"/> per the rules in the type
    /// remarks.
    /// </summary>
    /// <param name="id">Unique entry identifier. Must not be
    /// <see cref="Guid.Empty"/>.</param>
    /// <param name="utcTimestamp">UTC instant the operation was
    /// recorded. Must be of <see cref="DateTimeKind.Utc"/>.</param>
    /// <param name="userId">Identifier of the user, or
    /// <see langword="null"/> for system entries.</param>
    /// <param name="entityTypeName">Canonical type name of the affected
    /// entity. Must not be <see langword="null"/>, empty, or whitespace.</param>
    /// <param name="entityId">Canonical string form of the affected
    /// entity's identifier. Must not be <see langword="null"/>, empty,
    /// or whitespace.</param>
    /// <param name="action">The action that produced this entry.</param>
    /// <param name="before">JSON snapshot before the operation, shaped
    /// per <paramref name="action"/>.</param>
    /// <param name="after">JSON snapshot after the operation, shaped
    /// per <paramref name="action"/>.</param>
    /// <param name="correlationId">Correlation identifier. Must not be
    /// <see cref="Guid.Empty"/>.</param>
    /// <exception cref="ArgumentException">Thrown when any input fails
    /// validation, including when the Before/After shape does not match
    /// the supplied action.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when
    /// <paramref name="action"/> is not a defined
    /// <see cref="AuditAction"/> value.</exception>
    public AuditLogEntry(
        Guid id,
        DateTime utcTimestamp,
        Guid? userId,
        string entityTypeName,
        string entityId,
        AuditAction action,
        string? before,
        string? after,
        Guid correlationId)
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

        ArgumentException.ThrowIfNullOrWhiteSpace(entityTypeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);

        if (correlationId == Guid.Empty)
        {
            throw new ArgumentException(
                "CorrelationId must not be Guid.Empty.",
                nameof(correlationId));
        }

        ValidateSnapshotShape(action, before, after);

        Id = id;
        UtcTimestamp = utcTimestamp;
        UserId = userId;
        EntityTypeName = entityTypeName;
        EntityId = entityId;
        Action = action;
        Before = before;
        After = after;
        CorrelationId = correlationId;
    }

    private static void ValidateSnapshotShape(AuditAction action, string? before, string? after)
    {
        switch (action)
        {
            case AuditAction.Insert:
                if (before is not null)
                {
                    throw new ArgumentException(
                        "Insert audit entries must have Before = null.",
                        nameof(before));
                }
                if (after is null)
                {
                    throw new ArgumentException(
                        "Insert audit entries must have non-null After.",
                        nameof(after));
                }
                break;

            case AuditAction.Update:
            case AuditAction.Delete:
                if (before is null)
                {
                    throw new ArgumentException(
                        "Update and soft-Delete audit entries must have non-null Before.",
                        nameof(before));
                }
                if (after is null)
                {
                    throw new ArgumentException(
                        "Update and soft-Delete audit entries must have non-null After.",
                        nameof(after));
                }
                break;

            case AuditAction.HardDelete:
                if (before is null)
                {
                    throw new ArgumentException(
                        "HardDelete audit entries must have non-null Before.",
                        nameof(before));
                }
                if (after is not null)
                {
                    throw new ArgumentException(
                        "HardDelete audit entries must have After = null.",
                        nameof(after));
                }
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(action),
                    action,
                    "Unknown audit action.");
        }
    }
}
