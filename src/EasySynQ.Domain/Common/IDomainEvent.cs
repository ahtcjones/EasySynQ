namespace EasySynQ.Domain.Common;

/// <summary>
/// Marker interface for domain events. A domain event is an immutable
/// record of something that happened in the domain, intended to be
/// published transactionally with the state change that produced it
/// (per ADR 0008 §"Retraining cascade as a domain event").
/// </summary>
/// <remarks>
/// <para>
/// Implementations should be <see langword="record"/> types with only
/// value-type or immutable reference-type members so the event is safe
/// to enqueue, dispatch, and (eventually) serialize without observable
/// mutation between publish and handle.
/// </para>
/// <para>
/// The dispatcher infrastructure that publishes events implementing this
/// interface lives in <c>EasySynQ.Services.Events</c>; the integration
/// with EF Core's <c>SavingChangesAsync</c> pipeline lives in
/// <c>EasySynQ.Data.Interceptors.DomainEventDispatchInterceptor</c>.
/// </para>
/// </remarks>
public interface IDomainEvent
{
}
