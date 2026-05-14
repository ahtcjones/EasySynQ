using EasySynQ.Services.Abstractions;

namespace EasySynQ.Services.Identity;

/// <summary>
/// Extension of <see cref="ICurrentUserAccessor"/> that lets the shell
/// populate the active identity after a successful sign-in and clear it
/// on sign-out. The production implementation is intended to be
/// registered as a singleton so the same instance backs both interfaces
/// — the shell (and only the shell) writes; repositories, interceptors,
/// and signing flows read.
/// </summary>
/// <remarks>
/// <para>
/// This interface lives in <c>EasySynQ.Services.Identity</c> rather than
/// <c>EasySynQ.Services.Abstractions</c> on purpose. The read-only
/// <see cref="ICurrentUserAccessor"/> stays in <c>Abstractions</c>
/// because every layer depends on it; the writable extension is a
/// shell-consumer concern and does not belong in the abstraction set the
/// data layer pulls from.
/// </para>
/// </remarks>
public interface IWritableCurrentUserAccessor : ICurrentUserAccessor
{
    /// <summary>
    /// Replaces the active identity. Per ADR 0007, the snapshot captured
    /// at sign-in is the session-long source of truth — call this once
    /// per sign-in, never mid-session. Thread-safety is
    /// implementation-defined; the WPF implementation is
    /// dispatcher-bound by convention (every write originates on the
    /// UI thread).
    /// </summary>
    /// <param name="userId">Authenticated user's identifier. Must not
    /// be <see cref="Guid.Empty"/>.</param>
    /// <param name="username">Authenticated user's login identifier.
    /// Must not be <see langword="null"/>, empty, or whitespace.</param>
    /// <param name="displayName">Authenticated user's display name.
    /// Must not be <see langword="null"/>, empty, or whitespace.</param>
    /// <param name="roles">Role names captured at sign-in. Must not be
    /// <see langword="null"/>; may be empty.</param>
    /// <param name="permissions">Effective permission names captured at
    /// sign-in. Must not be <see langword="null"/>; may be empty.</param>
    /// <exception cref="ArgumentException">Thrown when
    /// <paramref name="userId"/> is <see cref="Guid.Empty"/>, or when
    /// <paramref name="username"/> or <paramref name="displayName"/> is
    /// null/empty/whitespace.</exception>
    /// <exception cref="ArgumentNullException">Thrown when
    /// <paramref name="roles"/> or <paramref name="permissions"/> is
    /// <see langword="null"/>.</exception>
    void SetCurrentUser(
        Guid userId,
        string username,
        string displayName,
        IReadOnlyCollection<string> roles,
        IReadOnlyCollection<string> permissions);

    /// <summary>
    /// Resets the accessor to its unauthenticated state. Used by
    /// sign-out and by initial app startup before the login window
    /// resolves.
    /// </summary>
    void Clear();
}
