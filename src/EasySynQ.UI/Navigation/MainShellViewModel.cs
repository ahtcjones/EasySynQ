using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using EasySynQ.Services.Abstractions;
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
    private readonly ICurrentUserAccessor _currentUser;

    /// <summary>Constructs the shell view model.</summary>
    /// <param name="logger">Diagnostic logger for the shell.</param>
    /// <param name="pulseDrawer">The Pulse drawer view model the
    /// shell hosts. Constructed by the caller so this constructor
    /// does not own the drawer's dependency graph.</param>
    /// <param name="currentUser">Read-only accessor for the
    /// signed-in user's session snapshot. Backs the topbar user-chip
    /// bindings (display name, initials, role list); never used for
    /// authorization decisions per ADR 0007 (those check
    /// <c>Permissions</c>, which is the data-layer's concern, not the
    /// shell's).</param>
    /// <exception cref="ArgumentNullException">Thrown when any
    /// argument is <see langword="null"/>.</exception>
    public MainShellViewModel(
        ILogger<MainShellViewModel> logger,
        PulseDrawerViewModel pulseDrawer,
        ICurrentUserAccessor currentUser)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(pulseDrawer);
        ArgumentNullException.ThrowIfNull(currentUser);
        _logger = logger;
        PulseDrawerViewModel = pulseDrawer;
        _currentUser = currentUser;
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
    /// Display name for the topbar's user chip. Pass-through read
    /// from <see cref="ICurrentUserAccessor.DisplayName"/>. Returns
    /// <see cref="string.Empty"/> in the unauthenticated state per
    /// the accessor's empty-state contract.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Change notification.</b> The accessor's properties do not
    /// implement <see cref="System.ComponentModel.INotifyPropertyChanged"/>,
    /// so a mid-session mutation (a future sign-out / re-sign-in
    /// flow) will not push an update to bindings. The current Phase 1
    /// flow resolves <see cref="MainShellViewModel"/> after sign-in
    /// (App's lazy <c>MainWindow</c> resolve) and the snapshot is
    /// fixed for the lifetime of the session per ADR 0007, so the
    /// missing notification is unobservable. When sign-out arrives in
    /// a later phase, the VM will need to relay accessor changes; the
    /// natural shape is to refresh by re-resolving the shell rather
    /// than wiring INPC through the accessor.
    /// </para>
    /// </remarks>
    public string UserDisplayName => _currentUser.DisplayName;

    /// <summary>
    /// Comma-joined role list for the topbar's user chip. Pass-through
    /// over <see cref="ICurrentUserAccessor.Roles"/>. ADR 0007 makes
    /// roles a collection (a single user may hold "Plant Manager" and
    /// "Internal Auditor" simultaneously); the chip subtitle is a
    /// flat string for layout reasons, so the collection is joined
    /// with ", " here. Phase 1 only ever produces a single-role
    /// Administrator, but the join handles any future multi-role
    /// shape without special-casing. Returns <see cref="string.Empty"/>
    /// in the unauthenticated state.
    /// </summary>
    /// <remarks>
    /// Display-only. ADR 0007 forbids role-name authorization checks;
    /// every authorization gate reads
    /// <see cref="ICurrentUserAccessor.Permissions"/> instead, and
    /// gating code never lives in the view-model layer.
    /// </remarks>
    public string CurrentRoles => string.Join(", ", _currentUser.Roles);

    /// <summary>
    /// Initials rendered inside the user-chip avatar. Derived from
    /// <see cref="UserDisplayName"/>: takes the first character of
    /// each whitespace- or punctuation-separated token, uppercases
    /// it, and truncates to two characters. Returns <c>"??"</c> when
    /// <see cref="UserDisplayName"/> is empty (the unauthenticated
    /// state) — the avatar still renders something rather than
    /// collapsing the chip layout.
    /// </summary>
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
