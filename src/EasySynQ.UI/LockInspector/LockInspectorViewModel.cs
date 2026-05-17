using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using EasySynQ.Domain.Entities.Audit;
using EasySynQ.Services.Abstractions;
using EasySynQ.Services.LockReasons;

namespace EasySynQ.UI.LockInspector;

/// <summary>
/// View model backing the "Why is this locked?" popover (ADR 0012
/// C7b). Construct one per trigger open with the
/// <c>(LockedEntityType, LockedEntityId)</c> pair that identifies the
/// entity whose lock chain is being inspected.
/// </summary>
/// <remarks>
/// <para>
/// <b>Always resolve, cache write-once.</b> Per the amended ADR 0012,
/// the VM always invokes the registered resolver on each
/// <see cref="LoadAsync"/> and renders the freshly-built chain. The
/// cache check via <see cref="ILockReasonRepository.GetByLockedEntityAsync"/>
/// is used only to decide whether to write through — first inspect
/// persists a <see cref="LockReason"/> row (audit evidence that a
/// lock was inspected at this time); subsequent inspects skip the
/// write. The cached row is never used as a render-time data source;
/// the resolver is cheap (1-2 indexed queries) and its output is the
/// authoritative shape every open.
/// </para>
/// <para>
/// <b>Not-locked surface.</b> When the resolver returns
/// <see langword="null"/> (entity unknown, or entity exists but is in
/// a non-locked state like <c>Draft</c>), the VM sets
/// <see cref="IsLocked"/> to <see langword="false"/> and exposes an
/// empty <see cref="Chain"/>. The popover XAML shows a friendly "not
/// locked" message in that state. The VM does NOT throw or surface an
/// error — a not-locked entity is a normal lookup result.
/// </para>
/// <para>
/// <b>No staleness comparison.</b> An earlier ADR draft proposed
/// comparing the cached row's <c>ModifiedUtc</c> against the live
/// entity's state to detect stale chains. The simpler always-resolve
/// pattern makes that comparison unnecessary — every open renders
/// fresh.
/// </para>
/// </remarks>
public sealed partial class LockInspectorViewModel : ObservableObject
{
    private readonly ILockReasonRepository _lockReasons;
    private readonly ILockReasonResolverRegistry _registry;

    /// <summary>Canonical lock-entity-type string (see
    /// <see cref="EasySynQ.Domain.LockedEntityTypes"/>).</summary>
    public string LockedEntityType { get; }

    /// <summary>Canonical string-form id of the entity whose lock
    /// state is being inspected.</summary>
    public string LockedEntityId { get; }

    /// <summary><see langword="true"/> while <see cref="LoadAsync"/>
    /// is in flight. The popover XAML can bind a spinner or progress
    /// indicator to this.</summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>Set to <see langword="true"/> after a successful
    /// resolve when the resolver returned a non-null chain;
    /// <see langword="false"/> when the entity is not locked or the
    /// resolver could not handle the type.</summary>
    [ObservableProperty]
    private bool _isLocked;

    /// <summary>The resolved chain, empty when not locked or before
    /// the first successful load.</summary>
    [ObservableProperty]
    private IReadOnlyList<LockReasonLink> _chain = [];

    /// <summary>
    /// Constructs the view model for a specific
    /// <c>(LockedEntityType, LockedEntityId)</c> pair.
    /// </summary>
    /// <param name="lockedEntityType">Canonical type string. Must
    /// not be <see langword="null"/>, empty, or whitespace.</param>
    /// <param name="lockedEntityId">Canonical string-form id. Must
    /// not be <see langword="null"/>, empty, or whitespace.</param>
    /// <param name="lockReasons">Repository for cache reads and the
    /// write-through path.</param>
    /// <param name="registry">Per-entity-type resolver registry.</param>
    /// <exception cref="ArgumentException">Thrown when either string
    /// argument is null, empty, or whitespace.</exception>
    /// <exception cref="ArgumentNullException">Thrown when either
    /// service argument is <see langword="null"/>.</exception>
    public LockInspectorViewModel(
        string lockedEntityType,
        string lockedEntityId,
        ILockReasonRepository lockReasons,
        ILockReasonResolverRegistry registry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockedEntityType);
        ArgumentException.ThrowIfNullOrWhiteSpace(lockedEntityId);
        ArgumentNullException.ThrowIfNull(lockReasons);
        ArgumentNullException.ThrowIfNull(registry);

        LockedEntityType = lockedEntityType;
        LockedEntityId = lockedEntityId;
        _lockReasons = lockReasons;
        _registry = registry;
    }

    /// <summary>
    /// Loads the chain for the bound
    /// <c>(LockedEntityType, LockedEntityId)</c> pair. Always invokes
    /// the resolver; writes a cached <see cref="LockReason"/> row on
    /// first inspect. Idempotent — subsequent invocations re-render
    /// the freshly-resolved chain and skip the write.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    [RelayCommand]
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        try
        {
            var resolver = _registry.GetResolver(LockedEntityType);
            if (resolver is null)
            {
                // Unknown type — no resolver registered. Treat as
                // "not locked" per ADR 0012's permissive registry
                // contract (the caller would otherwise have to
                // try/catch every open).
                IsLocked = false;
                Chain = [];
                return;
            }

            var freshChain = await resolver.ResolveAsync(
                LockedEntityId, cancellationToken);

            if (freshChain is null)
            {
                // Entity exists but is not currently in a locked state
                // (e.g., a Draft revision), or entity does not exist.
                IsLocked = false;
                Chain = [];
                return;
            }

            // Write-through: first inspect persists the row. Cache
            // lookup is the only purpose of the repository call —
            // rendering uses the fresh chain regardless of cache
            // state, so an existing cached row simply suppresses the
            // write (audit-row inflation guard).
            var cached = await _lockReasons.GetByLockedEntityAsync(
                LockedEntityType, LockedEntityId, cancellationToken);
            if (cached is null)
            {
                await _lockReasons.AddAsync(freshChain, cancellationToken);
                await _lockReasons.SaveChangesAsync(cancellationToken);
            }

            IsLocked = true;
            Chain = freshChain.Chain;
        }
        finally
        {
            IsLoading = false;
        }
    }
}
