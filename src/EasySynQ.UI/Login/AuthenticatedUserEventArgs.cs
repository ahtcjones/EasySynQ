using EasySynQ.Domain.Entities.Identity;

namespace EasySynQ.UI.Login;

/// <summary>
/// Event payload raised by <see cref="LoginViewModel"/> after a successful
/// authentication. Carries the authenticated <see cref="Domain.Entities.Identity.User"/>,
/// the change-password cue, and the session-long role and permission
/// snapshots resolved at sign-in (ADR 0007).
/// </summary>
/// <remarks>
/// The <see cref="User"/> property reuses the existing domain entity
/// rather than redefining a UI-side shape; the login surface is the
/// natural seam where the authenticated identity crosses into the
/// application, and a parallel DTO would duplicate every field for no
/// behavioral gain. <see cref="Roles"/> and <see cref="Permissions"/>
/// pass through unchanged from
/// <see cref="EasySynQ.Services.Identity.AuthenticationResult.Success"/>
/// so the App handler can hand them to
/// <see cref="EasySynQ.Services.Identity.IWritableCurrentUserAccessor.SetCurrentUser"/>
/// without re-resolving.
/// </remarks>
public sealed class AuthenticatedUserEventArgs : EventArgs
{
    /// <summary>The authenticated user.</summary>
    public User User { get; }

    /// <summary>
    /// Mirrors <c>User.MustChangePassword</c> at the moment the auth
    /// service returned <c>Success</c>. The shell should route to the
    /// change-password screen when <see langword="true"/>.
    /// </summary>
    public bool RequiresPasswordChange { get; }

    /// <summary>
    /// Role names captured at sign-in. Non-null; may be empty (a
    /// legitimate state — the authenticated user holds no in-effect
    /// role assignments — even though Phase 1 has no flow that
    /// produces it).
    /// </summary>
    public IReadOnlyCollection<string> Roles { get; }

    /// <summary>
    /// Effective permission names captured at sign-in. Non-null; may
    /// be empty (same rationale as <see cref="Roles"/>).
    /// </summary>
    public IReadOnlyCollection<string> Permissions { get; }

    /// <summary>Constructs the event payload.</summary>
    /// <param name="user">Authenticated user. Must not be
    /// <see langword="null"/>.</param>
    /// <param name="requiresPasswordChange">Change-password cue.</param>
    /// <param name="roles">Role-name snapshot. Must not be
    /// <see langword="null"/>; may be empty.</param>
    /// <param name="permissions">Permission-name snapshot. Must not be
    /// <see langword="null"/>; may be empty.</param>
    /// <exception cref="ArgumentNullException">Thrown when
    /// <paramref name="user"/>, <paramref name="roles"/>, or
    /// <paramref name="permissions"/> is
    /// <see langword="null"/>.</exception>
    public AuthenticatedUserEventArgs(
        User user,
        bool requiresPasswordChange,
        IReadOnlyCollection<string> roles,
        IReadOnlyCollection<string> permissions)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentNullException.ThrowIfNull(permissions);
        User = user;
        RequiresPasswordChange = requiresPasswordChange;
        Roles = roles;
        Permissions = permissions;
    }
}
