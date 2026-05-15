using System.Diagnostics.CodeAnalysis;

using EasySynQ.Domain.Common;

namespace EasySynQ.Services.Events;

/// <summary>
/// Handler for a specific <see cref="IDomainEvent"/> type. Implementations
/// are registered in DI and resolved by
/// <see cref="IDomainEventDispatcher"/> at dispatch time.
/// </summary>
/// <remarks>
/// <para>
/// <b>Phase 2 has no registered handlers.</b> The dispatcher infrastructure
/// is real and tested but every event published in Phase 2 (currently only
/// <c>DocumentRevisionApprovedEvent</c>) is dropped on the floor. Phase 4
/// (Competency Matrix) registers the first handler — for retraining
/// cascade — by adding an <c>IDomainEventHandler&lt;DocumentRevisionApprovedEvent&gt;</c>
/// implementation to DI.
/// </para>
/// <para>
/// <b>Handlers run inside the same <c>SaveChanges</c> as the publishing
/// transaction.</b> Implementations may stage further entity changes
/// (e.g., insert retraining-required rows) using the same
/// <c>EasySynQDbContext</c> + repositories the dispatcher's transaction
/// is wrapped around. Those staged changes are flushed by the same
/// SaveChanges that drained the event queue. A handler that throws
/// rolls the entire transaction back — including the original publishing
/// state change.
/// </para>
/// </remarks>
/// <typeparam name="TEvent">The event type the handler responds to.</typeparam>
[SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "ADR 0008 §\"Retraining cascade as a domain event\" prescribes a MediatR/Cap-style domain-event pipeline. " +
        "'EventHandler' is the canonical name for the handler half of that pattern; the analyzer flags the suffix because " +
        "it conflicts with the BCL System.EventHandler delegate convention, but here we are intentionally introducing the " +
        "domain-event-pipeline term, not a System.EventHandler-shaped delegate. Renaming would diverge from the established " +
        "convention every reader expects (similar precedent: CA1720 on DocumentReviewAssignmentStatus.Signed).")]
public interface IDomainEventHandler<in TEvent>
    where TEvent : IDomainEvent
{
    /// <summary>
    /// Handles the supplied event. May stage further entity changes
    /// against the active <c>EasySynQDbContext</c>; those changes are
    /// committed by the same <c>SaveChanges</c> as the event's
    /// publishing transaction.
    /// </summary>
    /// <param name="domainEvent">The event being handled.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken);
}
