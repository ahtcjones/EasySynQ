using EasySynQ.UI.Navigation;

namespace EasySynQ.UI.Placeholders;

/// <summary>
/// View model for the generic "coming in a future phase" placeholder.
/// Wraps a <see cref="NavigationItem"/> so the placeholder view can
/// bind the display name, target phase, and a hardcoded one-sentence
/// description per <see cref="ComingSoonDescriptions"/>.
/// </summary>
/// <remarks>
/// Implements <see cref="IDirtyStateAware"/> so the shell's
/// dirty-state guard treats the placeholder uniformly with future
/// real view models. Placeholders have no editable state, so the
/// implementation always reports clean and always allows discard.
/// </remarks>
public sealed class ComingSoonViewModel : IDirtyStateAware
{
    /// <summary>Constructs the placeholder over a catalog item.</summary>
    /// <param name="item">Navigation entry the placeholder represents.
    /// Must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when
    /// <paramref name="item"/> is <see langword="null"/>.</exception>
    /// <exception cref="KeyNotFoundException">Thrown when no
    /// description is registered for <paramref name="item"/>'s
    /// <see cref="NavigationItem.Id"/> — see
    /// <see cref="ComingSoonDescriptions"/>.</exception>
    public ComingSoonViewModel(NavigationItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        DisplayName = item.DisplayName;
        TargetPhase = item.TargetPhase;
        Description = ComingSoonDescriptions.DescribeOrThrow(item.Id);
    }

    /// <summary>Mirrors <c>NavigationItem.DisplayName</c>.</summary>
    public string DisplayName { get; }

    /// <summary>Mirrors <c>NavigationItem.TargetPhase</c>.</summary>
    public int TargetPhase { get; }

    /// <summary>Hardcoded one-sentence description of what the
    /// underlying module will do once its target phase ships.</summary>
    public string Description { get; }

    /// <inheritdoc />
    public bool HasUnsavedChanges { get; }

    /// <inheritdoc />
    public Task<bool> ConfirmDiscardAsync(CancellationToken cancellationToken)
        => Task.FromResult(true);
}
