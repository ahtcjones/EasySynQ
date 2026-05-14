using EasySynQ.Domain.Entities.Identity;

namespace EasySynQ.UI.Bootstrap;

/// <summary>
/// Event payload raised by <see cref="BootstrapViewModel"/> after a
/// successful first-run administrator creation. Carries the newly-
/// created administrator so the shell can pass it to
/// <c>IWritableCurrentUserAccessor.SetCurrentUser</c> as it
/// transitions to <c>MainWindow</c>. Mirrors
/// <see cref="EasySynQ.UI.Login.AuthenticatedUserEventArgs"/>'s shape
/// (single <see cref="User"/> property) minus the change-password
/// cue — bootstrap always sets <c>MustChangePassword = false</c> at
/// the service tier (the user just chose the password).
/// </summary>
public sealed class BootstrapSucceededEventArgs : EventArgs
{
    /// <summary>The newly-created Administrator user.</summary>
    public User Administrator { get; }

    /// <summary>Constructs the event payload.</summary>
    /// <param name="administrator">The Administrator user. Must not
    /// be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when
    /// <paramref name="administrator"/> is
    /// <see langword="null"/>.</exception>
    public BootstrapSucceededEventArgs(User administrator)
    {
        ArgumentNullException.ThrowIfNull(administrator);
        Administrator = administrator;
    }
}
