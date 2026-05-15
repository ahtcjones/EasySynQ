using EasySynQ.Services.Events;

using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EasySynQ.Data.Interceptors;

/// <summary>
/// Drains the per-scope <see cref="IDomainEventDispatcher"/> queue at
/// the start of every <c>SaveChanges</c> so that handler-staged entity
/// changes (Phase 4 retraining cascade and beyond) participate in the
/// same transaction as the originating state change (ADR 0008
/// §"Retraining cascade as a domain event"; ADR 0008 C3 plan §A1).
/// </summary>
/// <remarks>
/// <para>
/// <b>Order matters.</b> This interceptor must run BEFORE
/// <see cref="StandardFieldsInterceptor"/> and
/// <see cref="AuditSaveChangesInterceptor"/>. Handlers may add new
/// entities to the change tracker; those entities then receive
/// standard-field stamping (CreatedBy/CreatedUtc/...) from the
/// standard-fields interceptor and audit-row generation from the audit
/// interceptor, all in the same SaveChanges. Reversing this order
/// would skip those stages for handler-added rows. The order is
/// established in
/// <c>EasySynQDbContextOptionsExtensions.UseEasySynQInterceptors</c>.
/// </para>
/// <para>
/// <b>Lifetime.</b> The interceptor is scoped — it is constructed fresh
/// per DI scope so it shares the dispatcher instance with the services
/// in that scope (the lifecycle service that enqueued the event).
/// </para>
/// <para>
/// <b>No reentry.</b> If a handler enqueues additional events during
/// dispatch, those events are processed by the same draining loop in
/// <c>DispatchPendingAsync</c> — the dispatcher itself loops until the
/// queue is empty. The interceptor does not re-fire after the drain.
/// </para>
/// <para>
/// <b>Phase 2 default.</b> No handlers are registered in Phase 2.
/// <see cref="IDomainEventDispatcher.DispatchPendingAsync"/> drains the
/// queue without observable side effect; the publishing semantics are
/// pinned by tests.
/// </para>
/// </remarks>
public sealed class DomainEventDispatchInterceptor : SaveChangesInterceptor
{
    private readonly IDomainEventDispatcher _dispatcher;

    /// <summary>Constructs the interceptor over the scoped dispatcher.</summary>
    public DomainEventDispatchInterceptor(IDomainEventDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        _dispatcher = dispatcher;
    }

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (_dispatcher.HasPending)
        {
            // The synchronous SaveChanges path is not used by Phase 2
            // production code; the lifecycle service is fully async.
            // Provide a sync-shim for completeness (and so code paths
            // that DO call SaveChanges synchronously cannot silently
            // skip dispatch). GetAwaiter().GetResult() is acceptable
            // here because the only code reaching this branch is
            // sync-by-choice already.
            _dispatcher.DispatchPendingAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (_dispatcher.HasPending)
        {
            await _dispatcher.DispatchPendingAsync(cancellationToken);
        }
        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
