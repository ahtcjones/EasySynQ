namespace EasySynQ.UI.Navigation;

/// <summary>
/// UI-internal protocol between content view models and the shell's
/// navigation. A view model implements this when it holds edits that
/// would be lost if the user navigates away — the shell consults the
/// current content before swapping to a new view.
/// </summary>
/// <remarks>
/// <para>
/// This is deliberately not in <c>EasySynQ.Services</c> or
/// <c>EasySynQ.Domain</c>. "There are unsaved edits and the user might
/// abandon them" is a UX-layer concept; the domain has no business
/// modeling it and the service layer has no business owning the
/// confirmation prompt.
/// </para>
/// <para>
/// <b>Per-view wording.</b> <see cref="ConfirmDiscardAsync"/> belongs
/// to the implementing view model rather than the shell so each surface
/// can phrase its prompt appropriately — "Discard your in-progress
/// NCR?" reads better than a generic "Discard unsaved changes?" Each
/// view also knows what counts as "dirty" in its own context.
/// </para>
/// </remarks>
public interface IDirtyStateAware
{
    /// <summary>
    /// Whether the implementing view model has uncommitted edits at the
    /// moment the property is read. Polled by the shell only at
    /// navigation time, not continuously — implementations need not
    /// raise <c>INotifyPropertyChanged</c> for it.
    /// </summary>
    bool HasUnsavedChanges { get; }

    /// <summary>
    /// Prompts the user (typically via a modal dialog owned by the
    /// implementing view) to confirm discarding the pending edits.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token, propagated
    /// from the shell's navigation command.</param>
    /// <returns>
    /// <see langword="true"/> to allow navigation (edits will be
    /// discarded); <see langword="false"/> to cancel and keep the
    /// current view active.
    /// </returns>
    Task<bool> ConfirmDiscardAsync(CancellationToken cancellationToken);
}
