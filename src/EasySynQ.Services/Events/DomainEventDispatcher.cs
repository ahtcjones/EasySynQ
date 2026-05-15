using System.Reflection;
using System.Runtime.ExceptionServices;

using EasySynQ.Domain.Common;

using Microsoft.Extensions.DependencyInjection;

namespace EasySynQ.Services.Events;

/// <summary>
/// Per-scope <see cref="IDomainEventDispatcher"/> implementation. Resolves
/// <see cref="IDomainEventHandler{TEvent}"/> instances via the supplied
/// <see cref="IServiceProvider"/>; invokes them in registration order for
/// each enqueued event in enqueue order; clears the queue at the end of
/// a successful drain.
/// </summary>
/// <remarks>
/// <para>
/// <b>Choice of mechanism (ADR 0008 C3 plan §A1, §A2; not a new ADR per
/// §G Q11).</b> The interceptor-driven, per-scope-queue shape is chosen
/// to honor ADR 0008's transactional invariant for the Phase 4
/// retraining cascade: the dispatch must run in the same
/// <c>SaveChanges</c> as the operational state change that produced the
/// event, and must not depend on author discipline at every call site.
/// The dispatch interceptor in <c>EasySynQ.Data.Interceptors</c>
/// resolves this dispatcher from the active scope and calls
/// <see cref="DispatchPendingAsync"/> before
/// <c>StandardFieldsInterceptor</c> and
/// <c>AuditSaveChangesInterceptor</c> run, so handler-staged entity
/// changes pick up standard-field stamping and audit-row generation as
/// part of the same save.
/// </para>
/// <para>
/// <b>Reflection cost.</b> Handler resolution uses runtime type
/// construction (<c>typeof(IDomainEventHandler&lt;&gt;).MakeGenericType(eventType)</c>)
/// to handle each event by its concrete type. The cost is one
/// generic-type construction per dispatched event — negligible at the
/// expected event volume (a handful per save, never in tight loops).
/// </para>
/// </remarks>
public sealed class DomainEventDispatcher : IDomainEventDispatcher
{
    private readonly IServiceProvider _serviceProvider;
    private readonly Queue<IDomainEvent> _queue = new();

    /// <summary>Constructs the dispatcher over its scoped DI provider.</summary>
    public DomainEventDispatcher(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc />
    public void Enqueue(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _queue.Enqueue(domainEvent);
    }

    /// <inheritdoc />
    public bool HasPending => _queue.Count > 0;

    /// <inheritdoc />
    public async Task DispatchPendingAsync(CancellationToken cancellationToken)
    {
        // Drain in-order. Each event resolves its own handler set —
        // handlers may stage further entity changes (Phase 4 retraining
        // rows) which become part of the same SaveChanges transaction.
        // We deliberately do NOT re-enter the queue if a handler enqueues
        // additional events during dispatch; that's a Phase 4+ concern
        // when nested dispatch becomes a real shape.
        while (_queue.Count > 0)
        {
            var domainEvent = _queue.Dequeue();
            cancellationToken.ThrowIfCancellationRequested();
            await DispatchSingleAsync(domainEvent, cancellationToken);
        }
    }

    /// <inheritdoc />
    public void Clear() => _queue.Clear();

    private async Task DispatchSingleAsync(IDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        var eventType = domainEvent.GetType();
        var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(eventType);
        var handlers = _serviceProvider.GetServices(handlerType);

        foreach (var handler in handlers)
        {
            if (handler is null)
            {
                continue;
            }

            // The HandleAsync(TEvent, CancellationToken) signature is
            // resolved on the closed handler type. Reflection invoke
            // returns the Task; we await it. Phase 2 has zero handlers
            // registered, so this loop body never runs in practice
            // until Phase 4 lands its retraining handler.
            //
            // Synchronous throws from the handler bubble through
            // MethodInfo.Invoke wrapped in TargetInvocationException;
            // unwrap so callers (and tests) see the original exception
            // type. ExceptionDispatchInfo preserves the stack trace.
            var method = handlerType.GetMethod(nameof(IDomainEventHandler<IDomainEvent>.HandleAsync))!;
            Task task;
            try
            {
                task = (Task)method.Invoke(handler, [domainEvent, cancellationToken])!;
            }
            catch (TargetInvocationException tie) when (tie.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(tie.InnerException).Throw();
                throw; // unreachable; satisfies the compiler.
            }
            await task;
        }
    }
}
