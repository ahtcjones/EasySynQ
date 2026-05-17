namespace EasySynQ.UI.Printing;

/// <summary>
/// Service surface for the Document detail-view print flow (ADR 0008
/// C7 / SPEC §4.5). Assembles a snapshot of the document + latest
/// revision + signatures + reviewer comments + audit trail, builds a
/// printable <see cref="System.Windows.Documents.FlowDocument"/>, and
/// surfaces the system print dialog.
/// </summary>
/// <remarks>
/// <para>
/// <b>Snapshot at print-time.</b> The service re-fetches every input
/// row at call time so the printed artifact reflects the database
/// state at the moment of print, not a stale view-model cache. The
/// snapshot timestamp lands in the FlowDocument's footer so the
/// auditor can correlate the print with the audit log.
/// </para>
/// </remarks>
public interface IDocumentPrintService
{
    /// <summary>
    /// Assembles the snapshot, builds the FlowDocument, and presents
    /// the system print dialog modally. The user may select a printer
    /// or cancel; the service returns when the dialog closes.
    /// </summary>
    /// <param name="documentId">Identifier of the
    /// <see cref="EasySynQ.Domain.Entities.Documents.Document"/> to
    /// print. The latest revision is included in the snapshot
    /// automatically.</param>
    /// <param name="cancellationToken">Cancellation token honored
    /// during the data-fetch phase. Cancelling after the print dialog
    /// has been shown does not abort the system print process.</param>
    Task PrintAsync(Guid documentId, CancellationToken cancellationToken);
}
