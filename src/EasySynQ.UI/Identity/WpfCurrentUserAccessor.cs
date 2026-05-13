using EasySynQ.Domain.Entities.Identity;
using EasySynQ.Services.Abstractions;
using EasySynQ.Services.Identity;

namespace EasySynQ.UI.Identity;

/// <summary>
/// Production <see cref="IWritableCurrentUserAccessor"/> for the WPF
/// shell. Holds the active <see cref="User"/> and role-name snapshot,
/// exposing them through the read-only
/// <see cref="ICurrentUserAccessor"/> contract for downstream
/// consumers (the audit interceptor, standard-fields interceptor, and
/// signature service).
/// </summary>
/// <remarks>
/// <para>
/// <b>Threading.</b> No internal synchronization. The WPF dispatcher
/// is the discipline: <see cref="SetCurrentUser"/> and
/// <see cref="Clear"/> are invoked from the UI thread (LoginWindow's
/// success handler, the future sign-out command). Read access from
/// scoped interceptors happens on whatever thread the EF Core save
/// runs on — that thread sees a coherent snapshot because reference
/// reads of object/string fields are atomic on .NET, and the values
/// only mutate at known transitions (login / logout) that do not
/// overlap with database writes in the deployment topology.
/// </para>
/// <para>
/// <b>Empty-state contract.</b> Both
/// <see cref="UserDisplayName"/> and <see cref="CurrentRoleName"/>
/// return <see cref="string.Empty"/> when no user is signed in, per
/// the <see cref="ICurrentUserAccessor"/> documentation. The accessor
/// never surfaces <see langword="null"/> for those properties.
/// </para>
/// </remarks>
public sealed class WpfCurrentUserAccessor : IWritableCurrentUserAccessor
{
    private User? _user;
    private string? _roleName;

    /// <inheritdoc />
    public Guid? UserId => _user?.Id;

    /// <inheritdoc />
    public string UserDisplayName => _user?.DisplayName ?? string.Empty;

    /// <inheritdoc />
    public string CurrentRoleName => _roleName ?? string.Empty;

    /// <inheritdoc />
    public void SetCurrentUser(User user, string roleName)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(roleName);
        _user = user;
        _roleName = roleName;
    }

    /// <inheritdoc />
    public void Clear()
    {
        _user = null;
        _roleName = null;
    }
}
