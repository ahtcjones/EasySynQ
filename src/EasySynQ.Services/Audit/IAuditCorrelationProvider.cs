namespace EasySynQ.Services.Audit;

/// <summary>
/// Provides the correlation identifier stamped onto every
/// <c>AuditLogEntry</c> produced by a single user-facing operation.
/// </summary>
/// <remarks>
/// <para>
/// A logical operation (approve a document revision, sign a CoC, close an
/// NCR) typically produces multiple audit-log rows — the primary entity's
/// transition plus any cascaded effects. Sharing a correlation id across
/// those rows makes the operation reconstructable as a single business
/// event in audit queries.
/// </para>
/// <para>
/// The provider is intended to be **set by the calling code** (e.g., a UI
/// command handler) at the start of the logical operation, then cleared
/// when the operation completes. When <see cref="CurrentCorrelationId"/>
/// returns <see langword="null"/>, the audit interceptor falls back to a
/// fresh per-<c>SaveChanges</c> correlation id — every entity touched in
/// one save still shares an id, but consecutive saves get different ids.
/// </para>
/// </remarks>
public interface IAuditCorrelationProvider
{
    /// <summary>
    /// The correlation id currently in scope, or <see langword="null"/>
    /// if the audit interceptor should generate one per save.
    /// </summary>
    Guid? CurrentCorrelationId { get; }
}
