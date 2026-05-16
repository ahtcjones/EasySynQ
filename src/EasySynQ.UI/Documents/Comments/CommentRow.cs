namespace EasySynQ.UI.Documents.Comments;

/// <summary>
/// Single-row projection of a
/// <see cref="EasySynQ.Domain.Entities.Documents.DocumentReviewComment"/>
/// for the C6b comment panel (ADR 0008 C6b stop 6). Resolves the
/// soft-referenced <c>AuthorUserId</c> to a display name + username
/// at load time so the row renders without needing a second lookup
/// per item.
/// </summary>
/// <param name="CommentId">Identifier of the underlying comment.</param>
/// <param name="AuthorDisplayName">Resolved display name of the
/// comment author. Falls back to the username (or
/// <c>"(unknown user)"</c>) when the soft-referenced user can't be
/// resolved — e.g., a previously-existing user has been
/// soft-deleted.</param>
/// <param name="AuthorUsername">Resolved username of the comment
/// author, or <see langword="null"/> when not resolvable.</param>
/// <param name="BodyText">Comment body, verbatim.</param>
/// <param name="CreatedAtUtc">UTC instant the comment was
/// posted.</param>
public sealed record CommentRow(
    Guid CommentId,
    string AuthorDisplayName,
    string? AuthorUsername,
    string BodyText,
    DateTime CreatedAtUtc);
