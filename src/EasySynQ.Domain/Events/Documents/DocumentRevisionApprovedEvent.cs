using EasySynQ.Domain.Common;

namespace EasySynQ.Domain.Events.Documents;

/// <summary>
/// Published when a <c>DocumentRevision</c> transitions to the
/// <see cref="EasySynQ.Domain.Enums.DocumentLifecycle.Approved"/> state
/// (ADR 0008 §"Retraining cascade as a domain event"). Phase 2 has no
/// registered handler — the publishing infrastructure is exercised but
/// the event is dropped on the floor. Phase 4 (Competency Matrix)
/// registers the handler that writes retraining-required rows for
/// operators previously qualified on <see cref="PriorRevisionId"/>,
/// transactionally with the approval that produced this event.
/// </summary>
/// <param name="DocumentRevisionId">Id of the revision that just became
/// Approved.</param>
/// <param name="DocumentId">Id of the parent <c>Document</c>.</param>
/// <param name="PriorRevisionId">Id of the revision that was Active
/// immediately before this approval (the one being superseded), or
/// <see langword="null"/> if no prior Active revision existed (initial
/// approval of a brand-new document).</param>
/// <param name="ApprovedAtUtc">UTC instant of the approval transaction
/// — the same instant written to
/// <c>DocumentRevision.ApprovedAtUtc</c>.</param>
public sealed record DocumentRevisionApprovedEvent(
    Guid DocumentRevisionId,
    Guid DocumentId,
    Guid? PriorRevisionId,
    DateTime ApprovedAtUtc) : IDomainEvent;
