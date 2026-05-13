using EasySynQ.Services.Audit;

namespace EasySynQ.UI.Audit;

/// <summary>
/// Production <see cref="IAuditCorrelationProvider"/> for the WPF
/// shell, ground-state implementation. Returns <see langword="null"/>
/// unconditionally so the <c>AuditSaveChangesInterceptor</c> falls
/// back to its per-<c>SaveChanges</c> correlation-id generation —
/// every audit row produced by a single save shares an id, but
/// consecutive saves get different ids.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why null today.</b> Phase 1 has no command handler that spans
/// multiple <c>SaveChanges</c> calls within one logical user
/// operation; the per-save fallback is the correct granularity until
/// the first multi-save flow arrives.
/// </para>
/// <para>
/// <b>Replacement path.</b> Phase 2's Document Controller (approving
/// a document revision spans the revision update, signature insert,
/// and supersedence cascade) is the natural first consumer of
/// cross-save correlation. When it lands, this class is replaced
/// (not extended) by an <see cref="System.Threading.AsyncLocal{T}"/>-backed
/// implementation exposing a scope-disposable on the concrete type —
/// the interface itself stays unchanged.
/// </para>
/// </remarks>
public sealed class UiAuditCorrelationProvider : IAuditCorrelationProvider
{
    /// <inheritdoc />
    public Guid? CurrentCorrelationId => null;
}
