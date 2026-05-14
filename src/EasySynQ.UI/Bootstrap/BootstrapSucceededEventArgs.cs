using EasySynQ.Domain.Entities.Identity;

namespace EasySynQ.UI.Bootstrap;

/// <summary>
/// Event payload raised by <see cref="BootstrapViewModel"/> after a
/// successful first-run administrator creation. Carries the newly-created
/// administrator plus the role and permission snapshots returned by
/// <see cref="EasySynQ.Services.Bootstrap.IBootstrapService.CreateAdministratorAsync"/>
/// (ADR 0007) so the shell can pass them directly to
/// <c>IWritableCurrentUserAccessor.SetCurrentUser</c> as it transitions
/// to <c>MainWindow</c>. Mirrors
/// <see cref="EasySynQ.UI.Login.AuthenticatedUserEventArgs"/>'s shape
/// minus the change-password cue — bootstrap always sets
/// <c>MustChangePassword = false</c> at the service tier (the user just
/// chose the password).
/// </summary>
public sealed class BootstrapSucceededEventArgs : EventArgs
{
    /// <summary>The newly-created Administrator user.</summary>
    public User Administrator { get; }

    /// <summary>
    /// Role names captured at bootstrap — <c>["Administrator"]</c> for
    /// Phase 1. Non-null; non-empty for the happy path.
    /// </summary>
    public IReadOnlyCollection<string> Roles { get; }

    /// <summary>
    /// Permission names captured at bootstrap — the eleven Phase 1
    /// system permissions. Non-null; non-empty for the happy path.
    /// </summary>
    public IReadOnlyCollection<string> Permissions { get; }

    /// <summary>Constructs the event payload.</summary>
    /// <param name="administrator">The Administrator user. Must not be
    /// <see langword="null"/>.</param>
    /// <param name="roles">Role-name snapshot. Must not be
    /// <see langword="null"/>; may be empty.</param>
    /// <param name="permissions">Permission-name snapshot. Must not be
    /// <see langword="null"/>; may be empty.</param>
    /// <exception cref="ArgumentNullException">Thrown when
    /// <paramref name="administrator"/>, <paramref name="roles"/>, or
    /// <paramref name="permissions"/> is
    /// <see langword="null"/>.</exception>
    public BootstrapSucceededEventArgs(
        User administrator,
        IReadOnlyCollection<string> roles,
        IReadOnlyCollection<string> permissions)
    {
        ArgumentNullException.ThrowIfNull(administrator);
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentNullException.ThrowIfNull(permissions);
        Administrator = administrator;
        Roles = roles;
        Permissions = permissions;
    }
}
