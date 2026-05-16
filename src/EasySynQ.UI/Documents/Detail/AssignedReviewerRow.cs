using EasySynQ.Domain.Enums;

namespace EasySynQ.UI.Documents.Detail;

/// <summary>
/// Display projection of a
/// <see cref="EasySynQ.Domain.Entities.Documents.DocumentReviewAssignment"/>
/// row in the C6b assigned-reviewer panel (ADR 0008 C6b stop 7).
/// Resolves the soft-referenced reviewer to a display name + username
/// at load time so the row renders without per-row lookups.
/// </summary>
/// <param name="AssignmentId">Identifier of the underlying
/// assignment row.</param>
/// <param name="ReviewerDisplayName">Resolved display name. Falls
/// back to <c>"(unknown user)"</c> when the soft-referenced user is
/// unresolvable.</param>
/// <param name="ReviewerUsername">Resolved username, or
/// <see langword="null"/> when not resolvable.</param>
/// <param name="Status">Assignment status — Pending / Signed /
/// Discarded. Bound to a status badge whose color discipline
/// matches the spec (Pending = amber, Signed = green, Discarded
/// = neutral).</param>
public sealed record AssignedReviewerRow(
    Guid AssignmentId,
    string ReviewerDisplayName,
    string? ReviewerUsername,
    DocumentReviewAssignmentStatus Status);
