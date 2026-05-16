namespace EasySynQ.UI.Documents.EditMetadata;

/// <summary>
/// The user's confirmed edit from the
/// <see cref="IEditMetadataPrompter"/> dialog.
/// </summary>
/// <param name="NewNumber">Updated document number, non-empty.</param>
/// <param name="NewTitle">Updated document title, non-empty.</param>
public sealed record EditMetadataResult(string NewNumber, string NewTitle);
