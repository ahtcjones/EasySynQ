using EasySynQ.Domain.Entities.Audit;

namespace EasySynQ.Services.Abstractions;

/// <summary>
/// Audit-log-specific repository. Adds the lookup shapes the audit log
/// is queried by — entity-targeted, correlation-grouped, and time-range
/// scans — and overrides the mutation methods to enforce the
/// append-only invariant from SPEC §3.4.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IRepository{TEntity, TId}.SoftDelete"/> and
/// <see cref="IRepository{TEntity, TId}.HardDelete"/> throw
/// <see cref="InvalidOperationException"/> on this repository.
/// </para>
/// <para>
/// <b>Bypass caveat:</b> the repository's mutation guard only protects
/// callers that go through the repository. Code that injects
/// <c>EasySynQDbContext</c> directly can still call
/// <c>ctx.AuditLogEntries.Remove(entry)</c> or
/// <c>ctx.AuditLogEntries.Add(forgedRow)</c>. The Phase 1 mandate (per
/// CLAUDE.md and ADR 0002) is that services use repositories, not
/// direct <c>DbContext</c> access, for compliance-critical paths. A
/// future Roslyn analyzer that flags direct <c>AuditLogEntries</c>
/// access outside the audit interceptor would harden this further.
/// </para>
/// </remarks>
public interface IAuditLogRepository : IRepository<AuditLogEntry, Guid>
{
    /// <summary>
    /// Returns audit entries for a specific entity, matched by the
    /// short type name (<c>"User"</c>, <c>"Job"</c>, etc.) and the
    /// entity's string id.
    /// </summary>
    IQueryable<AuditLogEntry> ByEntity(string entityType, string entityId);

    /// <summary>
    /// Returns all audit entries sharing the supplied correlation id —
    /// the rows produced by a single logical operation.
    /// </summary>
    IQueryable<AuditLogEntry> ByCorrelation(Guid correlationId);

    /// <summary>
    /// Returns audit entries whose <c>UtcTimestamp</c> falls in the
    /// half-open interval <c>[fromUtc, toUtc)</c> — inclusive on
    /// <paramref name="fromUtc"/>, exclusive on <paramref name="toUtc"/>.
    /// Same convention as
    /// <see cref="EasySynQ.Domain.ValueObjects.EffectiveDateRange"/>.
    /// </summary>
    IQueryable<AuditLogEntry> InTimeRange(DateTime fromUtc, DateTime toUtc);
}
