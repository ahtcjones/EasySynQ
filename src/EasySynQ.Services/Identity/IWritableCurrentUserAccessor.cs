using EasySynQ.Domain.Entities.Identity;
using EasySynQ.Services.Abstractions;

namespace EasySynQ.Services.Identity;

/// <summary>
/// Extension of <see cref="ICurrentUserAccessor"/> that lets the shell
/// populate the active identity after a successful sign-in and clear
/// it on sign-out. The production implementation is intended to be
/// registered as a singleton so the same instance backs both
/// interfaces — the shell (and only the shell) writes; repositories,
/// interceptors, and signing flows read.
/// </summary>
/// <remarks>
/// <para>
/// This interface lives in <c>EasySynQ.Services.Identity</c> rather
/// than <c>EasySynQ.Services.Abstractions</c> on purpose. The
/// read-only <see cref="ICurrentUserAccessor"/> stays in
/// <c>Abstractions</c> because every layer depends on it; the
/// writable extension is a shell-consumer concern and does not belong
/// in the abstraction set the data layer pulls from.
/// </para>
/// </remarks>
public interface IWritableCurrentUserAccessor : ICurrentUserAccessor
{
    /// <summary>
    /// Replaces the active identity. Thread-safety is
    /// implementation-defined; the WPF implementation is
    /// dispatcher-bound by convention (every write originates on the
    /// UI thread).
    /// </summary>
    /// <param name="user">Authenticated user. Must not be
    /// <see langword="null"/>.</param>
    /// <param name="roleName">Canonical name of the role the user is
    /// acting under for this session. Snapshotted at sign-in time;
    /// becomes <see cref="ICurrentUserAccessor.CurrentRoleName"/>.
    /// Must not be <see langword="null"/>, empty, or whitespace.</param>
    void SetCurrentUser(User user, string roleName);

    /// <summary>
    /// Resets the accessor to its unauthenticated state. Used by
    /// sign-out and by initial app startup before the login window
    /// resolves.
    /// </summary>
    void Clear();
}
