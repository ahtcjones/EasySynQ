namespace EasySynQ.UI.Documents.CreateDocument;

/// <summary>
/// The user's confirmed input from the
/// <see cref="ICreateDocumentPrompter"/> dialog. Number and Title are
/// non-empty (the OK command is gated on it);
/// <see cref="SelectedPdfPath"/> is the optional initial PDF the user
/// picked (null when none).
/// </summary>
/// <param name="Number">Org-assigned document number, non-empty.</param>
/// <param name="Title">Document title, non-empty.</param>
/// <param name="SelectedPdfPath">Absolute path to the initial PDF
/// the user picked, or <see langword="null"/> when no PDF was
/// selected.</param>
public sealed record CreateDocumentResult(
    string Number,
    string Title,
    string? SelectedPdfPath);
