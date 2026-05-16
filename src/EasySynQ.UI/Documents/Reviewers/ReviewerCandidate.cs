namespace EasySynQ.UI.Documents.Reviewers;

/// <summary>
/// View-model row representing a candidate reviewer in the C6b
/// reviewer-picker. Carries the three fields the picker renders and
/// the picker VM filters on (<see cref="DisplayName"/>,
/// <see cref="Username"/>) plus the <see cref="Id"/> the consumer
/// dialog forwards to
/// <see cref="EasySynQ.Services.Documents.IDocumentLifecycleService.SubmitForReviewAsync"/>
/// as the reviewer-id set.
/// </summary>
/// <remarks>
/// <para>
/// <b>Record class with value-based equality on <see cref="Id"/>.</b>
/// The picker's <c>SelectedCandidates</c> collection contains
/// instances and is checked for membership when computing
/// command can-execute state and when re-applying selection after a
/// filter change. Equality by Id (rather than by reference) means
/// equivalent rows constructed from separate query results are
/// treated as the same candidate.
/// </para>
/// <para>
/// <b>No DB dependency.</b> The picker VM consumes a flat list of
/// these records, not <c>User</c> entities — the SubmitForReview
/// dialog (stop 3) is responsible for the projection from
/// <c>IUserRepository.GetUsersWithPermissionAsync</c> to this shape.
/// </para>
/// </remarks>
/// <param name="Id">Identifier of the candidate's <c>User</c> row.</param>
/// <param name="DisplayName">Human-readable display name shown
/// prominently in the picker row.</param>
/// <param name="Username">Username shown alongside DisplayName.
/// Filterable.</param>
public sealed record ReviewerCandidate(Guid Id, string DisplayName, string Username)
{
    /// <summary>Value-based equality on <see cref="Id"/>; two
    /// instances are equal when their ids match, regardless of
    /// display-name / username drift across query results.</summary>
    public bool Equals(ReviewerCandidate? other) =>
        other is not null && other.Id == Id;

    /// <inheritdoc />
    public override int GetHashCode() => Id.GetHashCode();
}
