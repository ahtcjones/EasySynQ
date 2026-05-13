using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using EasySynQ.UI.Pulse;

using Microsoft.Extensions.Logging;

namespace EasySynQ.UI.Navigation;

/// <summary>
/// View model for the application shell (<c>MainWindow</c>). Owns the
/// navigation tree state and mediates navigation events through the
/// dirty-state guard before swapping the active content.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why navigation is command-driven, not setter-driven.</b>
/// <see cref="SelectedItem"/>'s setter is plain INPC — it does not run
/// the dirty-state guard, write to <see cref="CurrentContent"/>, or
/// trigger any side effect beyond raising
/// <c>PropertyChanged</c>. All navigation flow has to go through
/// <see cref="NavigateToCommand"/> so the guard can intercept. The
/// shell view binds tree selection to the command, not back to the
/// property, so a rejected nav does not desync the selection state.
/// </para>
/// <para>
/// <b>CurrentContent.</b> Holds whatever object the
/// <see cref="System.Windows.Controls.ContentControl"/> in the shell is
/// currently showing. <see cref="CurrentContent"/> has a private
/// setter; only <see cref="NavigateToAsync"/> (and the
/// <see cref="SetCurrentContentForTesting"/> internal seam) write to
/// it. As of E2.4, <see cref="NavigateToAsync"/> assigns
/// <see cref="CurrentContent"/> via
/// <see cref="NavigationContentFactory.CreateContentFor"/> — placeholder
/// view models render every entry until real module views ship in
/// their owning phases.
/// </para>
/// </remarks>
public partial class MainShellViewModel : ObservableObject
{
    private readonly ILogger<MainShellViewModel> _logger;

    /// <summary>Constructs the shell view model.</summary>
    /// <param name="logger">Diagnostic logger for the shell.</param>
    /// <param name="pulseDrawer">The Pulse drawer view model the
    /// shell hosts. Constructed by the caller (Chunk E5 will resolve
    /// it from the DI host) so this constructor does not own the
    /// drawer's dependency graph — keeps the shell's own constructor
    /// stable as the drawer's dependencies evolve, and keeps every
    /// <see cref="NullLogger{T}"/>-style seam visible in
    /// <see cref="App"/> where the host wiring lives.</param>
    /// <exception cref="ArgumentNullException">Thrown when any
    /// argument is <see langword="null"/>.</exception>
    public MainShellViewModel(
        ILogger<MainShellViewModel> logger,
        PulseDrawerViewModel pulseDrawer)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(pulseDrawer);
        _logger = logger;
        PulseDrawerViewModel = pulseDrawer;
    }

    /// <summary>
    /// View model for the slide-out Pulse drawer. Exposed on the
    /// shell so XAML bindings reach the drawer's
    /// <see cref="PulseDrawerViewModel.ToggleDrawerCommand"/>,
    /// <see cref="PulseDrawerViewModel.RefreshTilesCommand"/>, and
    /// the topbar's bound severity counts.
    /// </summary>
    public PulseDrawerViewModel PulseDrawerViewModel { get; }

    /// <summary>
    /// The navigation entries to render, in catalog order. Always
    /// returns the same authoritative list — there is no mutation
    /// surface for adding or hiding items in this sub-chunk. Held as
    /// an instance auto-property (rather than a delegating expression
    /// body) so XAML <c>{Binding Items}</c> resolves through the
    /// DataContext without analyzers flagging the member as static.
    /// </summary>
    public IReadOnlyList<NavigationItem> Items { get; } = NavigationCatalog.AllItems;

    /// <summary>
    /// The currently-active navigation entry. Plain INPC; setting this
    /// does not invoke the dirty-state guard. Production callers must
    /// route nav requests through <see cref="NavigateToCommand"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentSectionLabel))]
    private NavigationItem? _selectedItem;

    private object? _currentContent;

    /// <summary>
    /// The content view model currently rendered in the shell's
    /// content host. See the type-level remarks for the writability
    /// discipline — only <see cref="NavigateToAsync"/> and the
    /// <see cref="SetCurrentContentForTesting"/> seam write to it.
    /// </summary>
    public object? CurrentContent
    {
        get => _currentContent;
        private set => SetProperty(ref _currentContent, value);
    }

    /// <summary>
    /// Test-only seam — assigns <see cref="CurrentContent"/> without
    /// going through the dirty-state guard. Do NOT call from production
    /// code. Exposed via <c>InternalsVisibleTo</c> to the test project
    /// so dirty-state-aware test fixtures can pre-populate content.
    /// E2.4 will replace test usage of this seam with real
    /// navigation-driven assignment.
    /// </summary>
    /// <param name="content">The content view model (or
    /// <see langword="null"/>) to install.</param>
    internal void SetCurrentContentForTesting(object? content) => CurrentContent = content;

    /// <summary>
    /// Section name of <see cref="SelectedItem"/>, or the empty string
    /// when nothing is selected. Used by the shell's breadcrumb /
    /// section-chip rendering.
    /// </summary>
    public string CurrentSectionLabel => SelectedItem?.Section.ToString() ?? string.Empty;

    /// <summary>
    /// Display name for the topbar's user chip. Dev placeholder for
    /// E2.2-B — Chunk E5 replaces this with a pass-through read from
    /// <c>ICurrentUserAccessor.UserDisplayName</c> once the accessor is
    /// constructor-injected via the host's DI graph.
    /// </summary>
    /// <remarks>
    /// Hardcoded to "M. Rodriguez" so the shell renders the prototype's
    /// user chip with realistic data during shell development. Do NOT
    /// add logic that depends on this value being a real signed-in user
    /// — it is not. Held as an instance auto-property so XAML
    /// <c>{Binding UserDisplayName}</c> resolves through the DataContext
    /// without analyzers flagging the member as static (same precedent
    /// as <see cref="Items"/>).
    /// </remarks>
    public string UserDisplayName { get; } = "M. Rodriguez";

    /// <summary>
    /// Role label for the topbar's user chip. Dev placeholder for E2.2-B
    /// — same E5 swap path as <see cref="UserDisplayName"/>; will read
    /// <c>ICurrentUserAccessor.CurrentRoleName</c> once DI is wired.
    /// </summary>
    public string CurrentRoleName { get; } = "Quality Manager";

    /// <summary>
    /// Initials rendered inside the user-chip avatar. Derived from
    /// <see cref="UserDisplayName"/>: takes the first character of
    /// each whitespace- or punctuation-separated token, uppercases
    /// it, and truncates to two characters. Returns <c>"??"</c> when
    /// <see cref="UserDisplayName"/> is empty.
    /// </summary>
    /// <remarks>
    /// Computed rather than stored so this property tracks
    /// <see cref="UserDisplayName"/> automatically. The underlying
    /// display name is the hardcoded E2.2-B dev placeholder; E5
    /// swaps <see cref="UserDisplayName"/> for an
    /// <c>ICurrentUserAccessor</c> pass-through and <c>UserInitials</c>
    /// inherits the change for free.
    /// </remarks>
    public string UserInitials
    {
        get
        {
            if (string.IsNullOrWhiteSpace(UserDisplayName))
            {
                return "??";
            }
            var tokens = UserDisplayName.Split(
                [' ', '.', ',', '-'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (tokens.Length == 0)
            {
                return "??";
            }
            var initials = string.Concat(tokens.Select(t => char.ToUpperInvariant(t[0])));
            return initials.Length <= 2 ? initials : initials[..2];
        }
    }

    /// <summary>
    /// Raised when the dirty-state guard denies navigation. The view
    /// uses this to roll any selection visual back to the previously
    /// selected item — the input gesture (e.g., a tree-node click)
    /// often updates the visual selection before the
    /// <see cref="SelectedItem"/> binding resolves, and that
    /// optimistic update has to be undone on rejection.
    /// </summary>
    public event EventHandler<NavigationCancelledEventArgs>? NavigationCancelled;

    /// <summary>
    /// Attempts to switch the shell to <paramref name="target"/>.
    /// Honors the dirty-state guard on the current
    /// <see cref="CurrentContent"/> before committing.
    /// </summary>
    /// <param name="target">The intended new navigation entry, or
    /// <see langword="null"/> to no-op.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [RelayCommand]
    private async Task NavigateToAsync(NavigationItem? target, CancellationToken cancellationToken)
    {
        if (target is null)
        {
            return;
        }

        // ReferenceEquals (rather than ==) is deliberate. NavigationItem
        // is a class with default equality semantics, so == compares
        // references — but ReferenceEquals makes the intent explicit
        // and survives a future override of Equals.
        if (ReferenceEquals(target, SelectedItem))
        {
            return;
        }

        if (CurrentContent is IDirtyStateAware dirty && dirty.HasUnsavedChanges)
        {
            var allow = await dirty
                .ConfirmDiscardAsync(cancellationToken)
                .ConfigureAwait(true);

            if (!allow)
            {
                LogNavigationCancelled(target.Id, target.Section);
                NavigationCancelled?.Invoke(this, new NavigationCancelledEventArgs(target));
                return;
            }
        }

        SelectedItem = target;
        CurrentContent = NavigationContentFactory.CreateContentFor(target);
    }

    /// <summary>
    /// Source-generated <see cref="LogLevel.Information"/> emit for the
    /// dirty-state-guard rejection. Uses the
    /// <see cref="LoggerMessageAttribute"/> pattern (CA1848 / .NET 6+
    /// recommendation) so the message template is compiled once;
    /// matches the precedent set by <c>LoginViewModel</c>.
    /// </summary>
    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Information,
        Message = "Navigation to {NavigationItemId} ({Section}) cancelled by dirty-state guard")]
    private partial void LogNavigationCancelled(string navigationItemId, NavigationSection section);
}
