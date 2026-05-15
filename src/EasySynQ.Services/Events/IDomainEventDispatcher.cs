using EasySynQ.Domain.Common;

namespace EasySynQ.Services.Events;

/// <summary>
/// Per-scope queue of <see cref="IDomainEvent"/> instances awaiting
/// dispatch, plus the dispatch operation itself (ADR 0008 C3 plan §A1,
/// §A2 — interceptor-driven dispatch over a per-scope queue).
/// </summary>
/// <remarks>
/// <para>
/// <b>Lifecycle.</b> Registered scoped — same lifetime as the
/// <c>EasySynQDbContext</c> and the per-scope unit of work. The queue is
/// owned by the dispatcher instance and lives for one DI scope. Services
/// inside that scope call <see cref="Enqueue(IDomainEvent)"/> from their
/// transactional code paths; the
/// <c>DomainEventDispatchInterceptor</c> resolves the dispatcher during
/// <c>SavingChangesAsync</c> and calls
/// <see cref="DispatchPendingAsync"/> to drain the queue and invoke
/// registered handlers. The dispatcher clears the queue at the end of a
/// successful drain so a subsequent <c>SaveChanges</c> in the same scope
/// does not re-fire prior events.
/// </para>
/// <para>
/// <b>No handler registered → silent no-op.</b> Phase 2 has no handlers
/// for any event type. A drain with no handlers consumes the queue
/// without observable side effect — the publishing infrastructure is
/// exercised so its semantics are pinned by tests, but no operational
/// state changes.
/// </para>
/// <para>
/// <b>Handler exceptions propagate.</b> A handler that throws aborts
/// <see cref="DispatchPendingAsync"/>, which aborts
/// <c>SavingChangesAsync</c>, which aborts the operational SaveChanges
/// transaction — every entity change made in the scope's transaction
/// rolls back together with the failed handler's staged changes. This
/// is the transactional guarantee ADR 0008 §"Retraining cascade as a
/// domain event" relies on for Phase 4's retraining cascade.
/// </para>
/// </remarks>
public interface IDomainEventDispatcher
{
    /// <summary>
    /// Enqueues <paramref name="domainEvent"/> for dispatch on the next
    /// drain. Order of enqueue is preserved through dispatch.
    /// </summary>
    /// <param name="domainEvent">The event to enqueue. Must not be
    /// <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when
    /// <paramref name="domainEvent"/> is <see langword="null"/>.</exception>
    void Enqueue(IDomainEvent domainEvent);

    /// <summary>
    /// Returns <see langword="true"/> when the queue holds at least one
    /// undispatched event. Used by the dispatch interceptor to short-
    /// circuit when there is nothing to do.
    /// </summary>
    bool HasPending { get; }

    /// <summary>
    /// Drains the queue and dispatches each event to every registered
    /// <see cref="IDomainEventHandler{TEvent}"/> for its type, in
    /// enqueue order. Returns when the queue is empty. The queue is
    /// cleared as part of the drain — a subsequent
    /// <see cref="DispatchPendingAsync"/> call returns immediately
    /// unless new events are enqueued in between.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DispatchPendingAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Empties the queue without dispatching. Reserved for cleanup paths
    /// (e.g., test teardown); production code paths use
    /// <see cref="DispatchPendingAsync"/>.
    /// </summary>
    void Clear();
}
