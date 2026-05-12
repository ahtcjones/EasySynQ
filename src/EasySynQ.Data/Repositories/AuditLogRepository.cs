using EasySynQ.Data.Context;
using EasySynQ.Domain.Entities.Audit;
using EasySynQ.Services.Abstractions;

namespace EasySynQ.Data.Repositories;

/// <summary>
/// Audit-log-specific repository. Adds entity-targeted, correlation-
/// grouped, and time-range query shapes; overrides the mutation methods
/// to enforce the append-only invariant from SPEC §3.4.
/// </summary>
public class AuditLogRepository : Repository<AuditLogEntry, Guid>, IAuditLogRepository
{
    /// <summary>Constructs the repository over the supplied DbContext.</summary>
    public AuditLogRepository(EasySynQDbContext context) : base(context)
    {
    }

    /// <inheritdoc />
    public IQueryable<AuditLogEntry> ByEntity(string entityType, string entityId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityType);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);

        return Context.AuditLogEntries
            .Where(a => a.EntityTypeName == entityType && a.EntityId == entityId);
    }

    /// <inheritdoc />
    public IQueryable<AuditLogEntry> ByCorrelation(Guid correlationId)
    {
        if (correlationId == Guid.Empty)
        {
            throw new ArgumentException("CorrelationId must not be Guid.Empty.", nameof(correlationId));
        }
        return Context.AuditLogEntries.Where(a => a.CorrelationId == correlationId);
    }

    /// <inheritdoc />
    public IQueryable<AuditLogEntry> InTimeRange(DateTime fromUtc, DateTime toUtc)
    {
        if (fromUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("fromUtc must have DateTimeKind.Utc.", nameof(fromUtc));
        }
        if (toUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("toUtc must have DateTimeKind.Utc.", nameof(toUtc));
        }

        // Inclusive on from, exclusive on to — same half-open convention as
        // EffectiveDateRange. A row stamped exactly at `fromUtc` is included;
        // one stamped exactly at `toUtc` is not.
        return Context.AuditLogEntries
            .Where(a => a.UtcTimestamp >= fromUtc && a.UtcTimestamp < toUtc);
    }

    /// <summary>
    /// Always throws — audit log is append-only (SPEC §3.4).
    /// </summary>
    public override void SoftDelete(AuditLogEntry entity)
        => throw new InvalidOperationException(
            "Audit log is append-only per SPEC §3.4. SoftDelete is not permitted on AuditLogEntry.");

    /// <summary>
    /// Always throws — audit log is append-only (SPEC §3.4).
    /// </summary>
    public override void HardDelete(AuditLogEntry entity)
        => throw new InvalidOperationException(
            "Audit log is append-only per SPEC §3.4. HardDelete is not permitted on AuditLogEntry.");
}
