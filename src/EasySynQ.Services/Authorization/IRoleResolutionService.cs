namespace EasySynQ.Services.Authorization;

/// <summary>
/// Filters the current user's roles by the permission they would need
/// to hold to sign for an action (ADR 0009). Used by the UI signature-
/// dialog flow to populate the role-picker with only the roles that
/// can legitimately attest to the action being signed.
/// </summary>
/// <remarks>
/// <para>
/// <b>Pure synchronous helper</b> over
/// <see cref="EasySynQ.Services.Abstractions.ICurrentUserAccessor.RolePermissions"/>.
/// No DB roundtrip — the role-permission map was captured at sign-in
/// (ADR 0009) and is read locally per call. Cost is bounded by the
/// number of roles the user holds (typically 1–3).
/// </para>
/// <para>
/// <b>Role-derived only.</b> Direct per-user permission grants
/// (<c>UserPermission</c>) are not represented in
/// <see cref="EasySynQ.Services.Abstractions.ICurrentUserAccessor.RolePermissions"/>;
/// a user whose only path to a gating permission is a direct grant
/// returns no eligible roles from this service. Phase 2 has no
/// operational use of direct grants. The UI's signature-role prompter
/// raises a clear error when no eligible role is available; the
/// upstream lifecycle-service permission check should fire first
/// for any user who lacks the gating permission entirely.
/// </para>
/// </remarks>
public interface IRoleResolutionService
{
    /// <summary>
    /// Returns the names of every role the current user holds that
    /// grants the supplied permission, in deterministic ordinal order
    /// (so the picker renders consistently across sessions).
    /// </summary>
    /// <param name="permissionName">Canonical permission name (e.g.,
    /// <c>"Document.Review"</c>). Reference
    /// <see cref="EasySynQ.Domain.PermissionNames"/> constants at call
    /// sites; the parameter type is <see langword="string"/> so this
    /// service has no compile-time dependency on the catalog
    /// constants.</param>
    /// <returns>Eligible role names; possibly empty.</returns>
    /// <exception cref="System.ArgumentException">Thrown when
    /// <paramref name="permissionName"/> is null, empty, or whitespace.</exception>
    IReadOnlyList<string> GetEligibleRolesForPermission(string permissionName);
}
