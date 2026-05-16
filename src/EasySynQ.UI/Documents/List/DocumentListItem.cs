using EasySynQ.Domain.Entities.Documents;
using EasySynQ.Domain.Enums;

namespace EasySynQ.UI.Documents.List;

/// <summary>
/// Row snapshot for the Document list view's grid. Built by
/// <see cref="DocumentListViewModel.LoadAsync"/> from a
/// <see cref="Document"/> + its latest <see cref="DocumentRevision"/>
/// + the author's resolved username.
/// </summary>
/// <remarks>
/// <para>
/// <b>Snapshot, not live.</b> The row carries the values projected
/// at load time. A refresh (e.g., after a Create) rebuilds the row
/// from a fresh repository read. There is no per-row observation of
/// the underlying entity — keeping the row a value-shaped record
/// avoids the WPF binding cost of property-changed notifications on
/// thousands of grid rows.
/// </para>
/// </remarks>
/// <param name="DocumentId">Parent document id — used for selection
/// drill-down to the detail view.</param>
/// <param name="Number">Org-assigned document number (display
/// column).</param>
/// <param name="Title">Document title (display column).</param>
/// <param name="CurrentRevisionLabel">Latest revision's label, e.g.,
/// <c>"Rev A"</c>. Empty when the document has no revisions (defensive;
/// should not occur post-Create).</param>
/// <param name="Lifecycle">Latest revision's lifecycle enum — kept
/// alongside <see cref="LifecycleDisplay"/> for sorting / filtering
/// hooks the C7+ surfaces may add.</param>
/// <param name="LifecycleDisplay">Human-readable lifecycle string
/// (computed once at projection time so the column binding is a
/// plain string).</param>
/// <param name="AuthorDisplay">Resolved author username, or a
/// placeholder when the user row was not found.</param>
public sealed record DocumentListItem(
    Guid DocumentId,
    string Number,
    string Title,
    string CurrentRevisionLabel,
    DocumentLifecycle Lifecycle,
    string LifecycleDisplay,
    string AuthorDisplay);
