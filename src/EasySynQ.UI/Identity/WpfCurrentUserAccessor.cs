using EasySynQ.Services.Abstractions;
using EasySynQ.Services.Identity;

namespace EasySynQ.UI.Identity;

/// <summary>
/// Production <see cref="IWritableCurrentUserAccessor"/> for the WPF
/// shell. Holds the active identity, role snapshot, and permission
/// snapshot — populated once at sign-in per ADR 0007 — and exposes them
/// through the read-only <see cref="ICurrentUserAccessor"/> contract for
/// downstream consumers (the audit interceptor, standard-fields
/// interceptor, signature service, and authorization-check call sites).
/// </summary>
/// <remarks>
/// <para>
/// <b>Storage shape.</b> Stores discrete fields, not a
/// <c>User</c> entity reference. The accessor is the
/// session-snapshot-at-sign-in surface (ADR 0007 §Snapshot at sign-in);
/// referencing a tracked entity would conflate the snapshot with
/// whatever state EF Core happens to be tracking for that user
/// elsewhere. The discrete-field layout aligns with the contract.
/// </para>
/// <para>
/// <b>Threading.</b> No internal synchronization. The WPF dispatcher is
/// the discipline: <see cref="SetCurrentUser"/> and <see cref="Clear"/>
/// are invoked from the UI thread (LoginWindow/BootstrapWindow success
/// handlers, the future sign-out command). Read access from scoped
/// interceptors happens on whatever thread the EF Core save runs on —
/// that thread sees a coherent snapshot because reference reads of
/// object/string fields are atomic on .NET, and the values only mutate
/// at known transitions (login / logout) that do not overlap with
/// database writes in the deployment topology.
/// </para>
/// <para>
/// <b>Empty-state contract.</b> All non-id properties return non-null
/// empties when no user is signed in:
/// <see cref="Username"/> / <see cref="DisplayName"/> return
/// <see cref="string.Empty"/>; <see cref="Roles"/> /
/// <see cref="Permissions"/> return empty collections.
/// </para>
/// </remarks>
public sealed class WpfCurrentUserAccessor : IWritableCurrentUserAccessor
{
    private Guid? _userId;
    private string _username = string.Empty;
    private string _displayName = string.Empty;
    private IReadOnlyCollection<string> _roles = [];
    private IReadOnlyCollection<string> _permissions = [];

    /// <inheritdoc />
    public Guid? UserId => _userId;

    /// <inheritdoc />
    public string Username => _username;

    /// <inheritdoc />
    public string DisplayName => _displayName;

    /// <inheritdoc />
    public IReadOnlyCollection<string> Roles => _roles;

    /// <inheritdoc />
    public IReadOnlyCollection<string> Permissions => _permissions;

    /// <inheritdoc />
    public void SetCurrentUser(
        Guid userId,
        string username,
        string displayName,
        IReadOnlyCollection<string> roles,
        IReadOnlyCollection<string> permissions)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("UserId must not be Guid.Empty.", nameof(userId));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentNullException.ThrowIfNull(permissions);

        _userId = userId;
        _username = username;
        _displayName = displayName;
        _roles = roles;
        _permissions = permissions;
    }

    /// <inheritdoc />
    public void Clear()
    {
        _userId = null;
        _username = string.Empty;
        _displayName = string.Empty;
        _roles = [];
        _permissions = [];
    }
}
