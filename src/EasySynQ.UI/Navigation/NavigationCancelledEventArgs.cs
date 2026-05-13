namespace EasySynQ.UI.Navigation;

/// <summary>
/// Raised by <see cref="MainShellViewModel.NavigationCancelled"/> when
/// the dirty-state guard rejects a navigation attempt. The shell view
/// uses this to roll back any selection-visual that already moved to
/// the attempted target — typical pattern for tree/list controls whose
/// selection updates on the input gesture before the bound
/// SelectedItem property has resolved.
/// </summary>
public sealed class NavigationCancelledEventArgs : EventArgs
{
    /// <summary>
    /// The navigation entry the user attempted to switch to. The view
    /// model's <see cref="MainShellViewModel.SelectedItem"/> remains on
    /// the previous entry — the view should restore visuals to match.
    /// </summary>
    public NavigationItem AttemptedTarget { get; }

    /// <summary>Constructs the payload.</summary>
    /// <param name="attemptedTarget">The rejected navigation target.
    /// Must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when
    /// <paramref name="attemptedTarget"/> is <see langword="null"/>.</exception>
    public NavigationCancelledEventArgs(NavigationItem attemptedTarget)
    {
        ArgumentNullException.ThrowIfNull(attemptedTarget);
        AttemptedTarget = attemptedTarget;
    }
}
