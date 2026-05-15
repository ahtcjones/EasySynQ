using EasySynQ.Domain.Entities.Identity;

namespace EasySynQ.Services.Bootstrap;

/// <summary>
/// Result of <see cref="IBootstrapService.CreateAdministratorAsync"/>.
/// Carries the just-persisted Administrator user plus the session-long
/// snapshot of role names and permission names so the caller can pass
/// them directly to <see cref="EasySynQ.Services.Identity.IWritableCurrentUserAccessor.SetCurrentUser"/>
/// without a second resolution round-trip (ADR 0007).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Roles"/> and <see cref="Permissions"/> are
/// deterministically known by the bootstrap path itself — the service
/// just wrote the rows that produce them. The shape lets the caller
/// avoid an avoidable EF query without coupling the call site to the
/// rows' contents (the integration tests pin equivalence between this
/// value and the result of
/// <see cref="EasySynQ.Services.Abstractions.IPermissionRepository.GetEffectivePermissionNamesForUserAsync"/>
/// for the new user).
/// </para>
/// </remarks>
/// <param name="Administrator">The persisted Administrator user.</param>
/// <param name="Roles">Role names captured at bootstrap time —
/// <c>["Administrator"]</c> for Phase 1. Non-null; non-empty for the
/// happy path.</param>
/// <param name="Permissions">Permission names captured at bootstrap
/// time — the eleven Phase 1 system permissions. Non-null; non-empty
/// for the happy path.</param>
/// <param name="RolePermissions">Per-role breakdown of role-derived
/// permissions (ADR 0009). For the happy bootstrap path this is
/// always <c>{ "Administrator" → PermissionNames.All }</c> — the
/// bootstrap creates exactly one role and links every system
/// permission to it. The signature dialog reads this map at sign-in
/// to filter the picker; bootstrap returns it explicitly so the
/// shell does not need a follow-up DB read.</param>
public sealed record BootstrapResult(
    User Administrator,
    IReadOnlyCollection<string> Roles,
    IReadOnlyCollection<string> Permissions,
    IReadOnlyDictionary<string, IReadOnlyCollection<string>> RolePermissions);
