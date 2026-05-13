using EasySynQ.Domain.Entities.Identity;

namespace EasySynQ.UI.Login;

/// <summary>
/// Event payload raised by <see cref="LoginViewModel"/> after a successful
/// authentication. Carries the authenticated <see cref="Domain.Entities.Identity.User"/>
/// plus the change-password cue surfaced by the auth service so the shell
/// can route the user to either the main window or a change-password
/// dialog.
/// </summary>
/// <remarks>
/// The <see cref="User"/> property reuses the existing domain entity rather
/// than redefining a UI-side shape; the login surface is the natural seam
/// where the authenticated identity crosses into the application, and a
/// parallel DTO would duplicate every field for no behavioral gain.
/// </remarks>
public sealed class AuthenticatedUserEventArgs : EventArgs
{
    /// <summary>The authenticated user.</summary>
    public User User { get; }

    /// <summary>
    /// Mirrors <c>User.MustChangePassword</c> at the moment the auth service
    /// returned <c>Success</c>. The shell should route to the change-password
    /// screen when <see langword="true"/>.
    /// </summary>
    public bool RequiresPasswordChange { get; }

    /// <summary>Constructs the event payload.</summary>
    /// <param name="user">Authenticated user. Must not be <see langword="null"/>.</param>
    /// <param name="requiresPasswordChange">Change-password cue.</param>
    /// <exception cref="ArgumentNullException">Thrown when
    /// <paramref name="user"/> is <see langword="null"/>.</exception>
    public AuthenticatedUserEventArgs(User user, bool requiresPasswordChange)
    {
        ArgumentNullException.ThrowIfNull(user);
        User = user;
        RequiresPasswordChange = requiresPasswordChange;
    }
}
