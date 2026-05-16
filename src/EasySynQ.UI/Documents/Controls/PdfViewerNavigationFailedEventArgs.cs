namespace EasySynQ.UI.Documents.Controls;

/// <summary>
/// Raised by <see cref="PdfViewerControl"/> when a navigation to a
/// PDF document fails (ADR 0010 C5). Carries the attempted path,
/// a string-coded <see cref="Reason"/> discriminator the consumer
/// can branch on without parsing exception messages, and the
/// underlying <see cref="System.Exception"/> when one is available.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why <see cref="Reason"/> is a string, not an enum.</b> The set
/// of failure reasons may grow as the viewer integration matures
/// (C5 covers the obvious cases; later commits may surface more —
/// network failures if assets are ever fetched, plugin failures, etc.).
/// A string discriminator keeps the event-args contract stable across
/// version changes; consumers that switch on it use a default arm to
/// handle "any other reason." Matches the project's existing
/// <c>AuditAction</c>-style discriminator pattern in spirit
/// (string-coded so the wire shape is not enum-ordinal-coupled).
/// </para>
/// <para>
/// <b>Reason values defined for C5:</b>
/// <list type="bullet">
///   <item><c>"FileNotFound"</c> — the supplied DocumentPath does
///   not exist on disk.</item>
///   <item><c>"ViewerAssetsMissing"</c> — the bundled PDF.js
///   viewer.html cannot be located in the application directory.</item>
///   <item><c>"WebView2InitFailed"</c> — WebView2's
///   <c>EnsureCoreWebView2Async</c> threw or otherwise did not
///   complete initialization.</item>
///   <item><c>"NavigationCompletedWithError"</c> — WebView2 reported
///   a non-success result from its navigation pipeline.</item>
///   <item><c>"Unknown"</c> — any other failure shape.</item>
/// </list>
/// </para>
/// </remarks>
public sealed class PdfViewerNavigationFailedEventArgs : EventArgs
{
    /// <summary>The PDF path the viewer tried to load, or
    /// <see langword="null"/> if the failure happened before a path
    /// was set (e.g., WebView2 initialization itself failed).</summary>
    public string? AttemptedPath { get; }

    /// <summary>String-coded failure reason; see the type-level
    /// remarks for the C5 vocabulary.</summary>
    public string Reason { get; }

    /// <summary>Underlying exception if one was caught, otherwise
    /// <see langword="null"/>.</summary>
    public Exception? Exception { get; }

    /// <summary>Constructs the event payload.</summary>
    /// <param name="attemptedPath">The PDF path being attempted, or
    /// <see langword="null"/>.</param>
    /// <param name="reason">A non-null/empty/whitespace reason
    /// discriminator.</param>
    /// <param name="exception">Underlying exception if available.</param>
    /// <exception cref="ArgumentException">Thrown when
    /// <paramref name="reason"/> is null/empty/whitespace.</exception>
    public PdfViewerNavigationFailedEventArgs(
        string? attemptedPath,
        string reason,
        Exception? exception)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        AttemptedPath = attemptedPath;
        Reason = reason;
        Exception = exception;
    }
}
