namespace EasySynQ.Services.Abstractions;

/// <summary>
/// Exposes the identity, role membership, and effective permission set of
/// the user currently performing an operation (ADR 0007). Snapshotted at
/// sign-in time: the values do not change until the next sign-in (a
/// mid-session role or permission grant takes effect on next login per
/// ADR 0007 §Snapshot at sign-in, not live re-resolution).
/// </summary>
/// <remarks>
/// <para>
/// <b>Empty-state contract.</b> The unauthenticated state returns
/// <see cref="UserId"/> = <see langword="null"/>, <see cref="Username"/>
/// = <see cref="string.Empty"/>, <see cref="DisplayName"/>
/// = <see cref="string.Empty"/>, and both
/// <see cref="Roles"/> / <see cref="Permissions"/> as
/// <em>empty (but non-null)</em> collections. Consumers can rely on the
/// strings and collections never being <see langword="null"/>; only the
/// <see cref="UserId"/> uses the absence-of-value
/// <see langword="null"/> idiom (consistent with the prior contract).
/// </para>
/// <para>
/// <b>Authorization vs. display.</b>
/// <see cref="Permissions"/> is the source of truth for every
/// authorization check (<c>if (!Permissions.Contains(PermissionNames.X))
/// throw …</c>). <see cref="Roles"/> is for display only — the topbar's
/// user-chip subtitle and the like; role names never gate behavior. The
/// only Phase 1 deviation from "roles are display-only" is the
/// <see cref="EasySynQ.Services.Signatures.ISignatureService"/>, which
/// captures one role string per signature for audit attribution
/// (<c>Signature.RoleAtTimeOfSign</c>) and requires exactly one role on
/// the current user for the duration of Phase 1.
/// </para>
/// <para>
/// <b>Audit attribution.</b> Consumed by the data layer's
/// <c>StandardFieldsInterceptor</c> (to stamp <c>CreatedBy</c> /
/// <c>ModifiedBy</c> from <see cref="UserId"/>) and
/// <c>AuditSaveChangesInterceptor</c> (to populate
/// <c>AuditLogEntry.UserId</c>). Both interceptors only read
/// <see cref="UserId"/>; the other properties are for the application
/// surface.
/// </para>
/// </remarks>
public interface ICurrentUserAccessor
{
    /// <summary>
    /// Identifier of the current user, or <see langword="null"/> when no
    /// user is authenticated.
    /// </summary>
    Guid? UserId { get; }

    /// <summary>
    /// Username of the current user (the login identifier), or
    /// <see cref="string.Empty"/> when no user is authenticated.
    /// </summary>
    string Username { get; }

    /// <summary>
    /// Display name of the current user, or <see cref="string.Empty"/>
    /// when no user is authenticated.
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Names of every <see cref="EasySynQ.Domain.Entities.Identity.Role"/>
    /// the current user holds, captured at sign-in time. Empty (but never
    /// <see langword="null"/>) when no user is authenticated, or when the
    /// authenticated user holds no in-effect role assignments.
    /// </summary>
    /// <remarks>
    /// Display-only at authorization sites. The signature service is the
    /// only Phase 1 consumer that reads this for non-display purposes —
    /// see the type-level remarks on the "exactly one role" constraint.
    /// </remarks>
    IReadOnlyCollection<string> Roles { get; }

    /// <summary>
    /// Names of every
    /// <see cref="EasySynQ.Domain.Entities.Identity.Permission"/> the
    /// current user holds, transitively unioned across role membership
    /// and direct per-user grants and deduplicated. Captured at sign-in
    /// time. Empty (but never <see langword="null"/>) when no user is
    /// authenticated, or when the authenticated user holds no
    /// in-effect permissions.
    /// </summary>
    /// <remarks>
    /// Source of truth for authorization checks. Call sites should
    /// reference the string constants in
    /// <see cref="EasySynQ.Domain.PermissionNames"/>, never raw string
    /// literals (ADR 0007 §Decision).
    /// </remarks>
    IReadOnlyCollection<string> Permissions { get; }
}
