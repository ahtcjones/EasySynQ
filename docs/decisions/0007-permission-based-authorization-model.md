# ADR 0007 — Permission-Based Authorization Model

**Status:** Accepted
**Date:** 2026-05-13 (Proposed), 2026-05-14 (Accepted)
**Supersedes:** None (replaces an uncommitted "Role Resolution" draft of this ADR.)
**Related:** ADR 0006 (created the bootstrap User + Administrator Role + UserRole rows that this model extends.)

---

## Context

SPEC §3.4 currently describes authorization as "role-based" with a hardcoded role list (Operator, Lab Tech, Quality Manager, Auditor, Administrator). That description does not match how a real small-shop QMS is operated. The pilot deployment surfaces the mismatch concretely: the Production Manager runs the QMS with the Plant Manager's blessing under explicitly granted authorities; a Lead Operator is trained and certified as an internal auditor and acts in that capacity on specific reviews; the same Lead Operator does not get auditing capabilities passed to the 2nd/3rd shift Lead Operators because they have not been trained. Authorization in this environment is *what an individual is trained, certified, and authorized to do* — not what their job title says.

Title-based authorization (role-name checks like `Roles.Contains("QualityManager")`) breaks under this reality almost immediately. The model that fits is permission-based:

- **Permissions** are the unit of authorization. Code asks "may this user approve a document?" not "is this user a Quality Manager?"
- **Roles** are admin-defined named bundles of permissions. The deployment defines its own roles ("Plant Manager", "Maintenance Manager", "Lead Operator"); the spec does not prescribe them.
- **Per-user permission grants** supplement role-derived permissions. They model the everyday reality of "this user is trained and authorized for X outside their default role."

The Administrator role is reserved by the system — the bootstrap path of ADR 0006 creates it — and carries only system-administration permissions (create roles, create users, assign permissions, audit-log read). It is intentionally an IT-side role, not an operational superuser. An administrator who needs to approve a document is granted the relevant operational role (or specific permission) explicitly, the same way any other user would be.

This ADR defines the authorization data model, the resolution algorithm, the in-process accessor shape, and the Phase 1 scope. It does not specify the full permission catalog beyond Phase 1; subsequent phases add their permissions in their own migrations. It does not specify the admin UI for managing roles and permissions; that is deferred to its own ADR paired with the admin-UI work.

The implementation amends SPEC §3.4 to describe authorization as permission-based with role bundles, in the same commit (per CLAUDE.md Spec Drift rules).

## Decision

### Three new entities; two new effective-dated link tables

```
Permission
  Id           Guid
  Name         string   -- unique, "Resource.Action" form, e.g. "Document.Approve"
  Description  string
  Category     string   -- "System", "Document", "Production", ... — for UI grouping

RolePermission
  Id               Guid
  RoleId           Guid (FK Role)
  PermissionId     Guid (FK Permission)
  EffectivePeriod  EffectiveDateRange (owned type — EffectiveFromUtc, EffectiveToUtc?)

UserPermission
  Id               Guid
  UserId           Guid (FK User)
  PermissionId     Guid (FK Permission)
  EffectivePeriod  EffectiveDateRange (owned type — EffectiveFromUtc, EffectiveToUtc?)
```

Effective dating applies to both link tables. This is mandatory per SPEC §3.7 — the role's permission set and a user's direct grants are configuration that affects compliance evaluation, so historical questions ("what was Plant Manager authorized to do on 2026-01-15?", "was this user authorized to sign this approval on this date?") must be answerable from the schema alone, without log reconstruction.

`Permission.Name` is unique and matches the canonical string used in code. The `Resource.Action` form (`Document.Approve`, `Role.Create`) is convention, enforced by code review.

### Permissions are additive, never subtractive

A user's effective permission set at a given UTC instant is the union of:

1. Every permission attached to every role the user holds at that instant (via `UserRole → RolePermission`).
2. Every permission directly granted to the user at that instant (via `UserPermission`).

Per-user grants only add to the user's authorities. They cannot subtract a permission that a role would otherwise grant. The "this user has the QM role but is suspended from approving documents pending retraining" case is real but adds material complexity (a denied-permissions table, subtractive resolution, audit semantics for denial); deferred to a future ADR if and when the case becomes concrete.

### Permission catalog is code-defined, seeded by migration

Permission names correspond to code-level capabilities — a permission exists if and only if there is a code path that checks for it. They are not user-editable. The Phase 1 migration that creates the `Permission` table inserts the Phase 1 permission rows in the same migration. Each subsequent phase adds its permissions in its own migration.

A `PermissionNames` static class in `EasySynQ.Domain` mirrors the seeded catalog with code-side constants:

```csharp
public static class PermissionNames
{
    public const string SystemAdminister      = "System.Administer";
    public const string RoleCreate            = "Role.Create";
    public const string RoleEdit              = "Role.Edit";
    public const string RoleDelete            = "Role.Delete";
    public const string RoleAssignPermissions = "Role.AssignPermissions";
    public const string UserCreate            = "User.Create";
    public const string UserEdit              = "User.Edit";
    public const string UserDisable           = "User.Disable";
    public const string UserAssignRoles       = "User.AssignRoles";
    public const string UserGrantPermissions  = "User.GrantPermissions";
    public const string AuditLogRead          = "AuditLog.Read";
}
```

Every authorization check site refers to a `PermissionNames` constant; raw string literals are forbidden outside the constants class and the seeding migration. Enforcement is by convention and code review; an analyzer rule is deferred until misuse appears.

### Bootstrap grants Administrator only system permissions

ADR 0006's bootstrap path creates User + Administrator Role + UserRole atomically. This ADR extends that to also create RolePermission rows linking the Administrator role to every Phase 1 system permission (the eleven entries above), all in the same `IUnitOfWork.SaveChangesAsync`.

Administrator does **not** receive operational permissions like `Document.Approve` — those are linked to operational roles that the administrator creates after sign-in. The "Administrator is IT support, not the operational superuser" intent is enforced by the seeded permission set, not by convention.

Audit-row count grows from F1's 4 rows to approximately 26 in one transaction (1 User + 1 Role + 11 RolePermission + 11 owned-type rows for the RolePermissions' EffectivePeriod + 1 UserRole + 1 owned-type for the UserRole's EffectivePeriod), all sharing one CorrelationId via the per-save fallback. This is correct: every permission grant is auditable, which is exactly what an external assessor would want to see.

### Resolution lives on `IPermissionRepository`

```csharp
public interface IPermissionRepository : IRepository<Permission, Guid>
{
    Task<IReadOnlyList<string>> GetEffectivePermissionNamesForUserAsync(
        Guid userId,
        DateTime asOfUtc,
        CancellationToken ct);
}
```

The implementation issues a single EF Core query: `UserRole`-join-`RolePermission`-join-`Permission` filtered by effective period at `asOfUtc`, unioned with `UserPermission`-join-`Permission` filtered the same way, projected to `Permission.Name` and deduplicated. Returns `IReadOnlyList<string>`.

Role-name lookup stays on its own repository:

```csharp
public interface IUserRoleRepository : IRepository<UserRole, Guid>
{
    Task<IReadOnlyList<string>> GetEffectiveRoleNamesAsync(
        Guid userId,
        DateTime asOfUtc,
        CancellationToken ct);
}
```

Two repositories, two single-purpose methods. The auth service calls both when assembling the success result.

### `IClock` is passed through, not captured

Both repository methods take `asOfUtc` as a parameter. Callers (auth service and bootstrap service) capture `_clock.UtcNow` once and use the same instant for both calls — and, for bootstrap, for any related writes (`EffectivePeriod.EffectiveFromUtc`) in the same operation. The repositories do not call `DateTime.UtcNow` directly. This is the project's first non-query-filter consumer of effective dating and the pattern matters.

### `ICurrentUserAccessor` carries both sets; authorization checks against permissions

```csharp
public interface ICurrentUserAccessor
{
    Guid? UserId { get; }
    string? Username { get; }
    string? DisplayName { get; }
    IReadOnlyCollection<string> Roles { get; }       // for display ("Plant Manager")
    IReadOnlyCollection<string> Permissions { get; } // for authorization

    void SetCurrentUser(
        Guid userId,
        string username,
        string displayName,
        IReadOnlyCollection<string> roles,
        IReadOnlyCollection<string> permissions);
    void Clear();
}
```

Authorization checks at call sites are permission-membership tests:

```csharp
if (!_currentUser.Permissions.Contains(PermissionNames.DocumentApprove))
    throw new UnauthorizedOperationException(...);
```

Role names are display-only and never gate behavior. There is no `PrimaryRole` getter; how the topbar displays a multi-role user is a view concern decided when the topbar is built.

### Snapshot at sign-in, not live re-resolution

The role set and permission set captured at sign-in are fixed for the session. A user whose role membership or direct grants change mid-session does not see the change until next sign-in. Phase 1 has no UI for granting permissions mid-session, so the distinction is unobservable today; the snapshot is the simpler default and matches the SPEC §3.4 signature semantic ("role-at-time-of-sign"). Live re-resolution can be introduced via a future ADR, likely paired with an explicit "your authorities have changed; please re-sign-in" prompt rather than silent re-resolve.

### `AuthenticationResult.Success` and event-args carry both sets

- `AuthenticationResult.Success` gains `IReadOnlyCollection<string> Roles` and `IReadOnlyCollection<string> Permissions`.
- `AuthenticatedUserEventArgs` gains the same fields.
- `BootstrapSucceededEventArgs` gains the same fields.
- `App.OnLoginSucceeded` and `App.OnBootstrapSucceeded` stop passing the literal `"Authenticated User"` to `SetCurrentUser` and pass both collections through.

### SPEC §3.4 amendment

Same commit that ships the implementation amends SPEC §3.4 to:

> **Authorization:** Permission-based, with named role bundles. Permissions are the unit of authorization at every call site; roles are admin-defined bundles of permissions. Per-user permission grants (effective-dated) supplement role-derived permissions to support training-and-certification-based authorities outside the user's default role. The system reserves the Administrator role for IT-side operations (creating roles, creating users, assigning permissions, audit-log read); operational permissions are assigned by an administrator to operational roles after first run. Effective dating applies to role assignments, role-permission links, and user-permission grants — historical compliance questions are evaluated against the configuration in effect at the event timestamp (SPEC §3.7).

Spec revision bumps per CLAUDE.md.

## Alternatives Considered

### Pure role-based authorization (the prior ADR 0007 draft)

Authorization checks against role names directly (`Roles.Contains("QualityManager")`). Simpler — no permission catalog, no link tables. Rejected because it does not survive the deployment reality: real users routinely act outside their job title under specific authorities, and roles like "Production Manager who runs the QMS" or "Lead Operator who is also an internal auditor" cannot be cleanly named without inventing one role per individual. The permission-based model gets that flexibility for the cost of one extra entity, two link tables, and one migration.

### Subtractive per-user grants

Per-user grants can both add and revoke — a denied-permissions table that subtracts from the role-derived set. Models "this user is suspended from approving documents pending retraining" without removing the role. Rejected for Phase 1 because (a) the case is not concrete yet, (b) subtractive resolution materially complicates the resolution algorithm and the audit story (a denial is itself a grant that needs effective dating and audit), and (c) the additive-only model can be extended later without breaking existing data.

### Permission catalog stored as user-editable data

Treat permissions like roles — admins can create new permission names through the UI. Rejected because permission names are *code identifiers*: a permission exists only if there is a code path that checks for it. A permission row with no code reference is dead data; a code path that checks a permission name not in the catalog is a bug. Coupling the catalog to migrations enforces the relationship at the version-control level.

### Resolution on a domain service rather than a repository

Introduce `IUserAuthorizationResolver` (a service) that computes effective permissions, consuming both `IUserRoleRepository` and `IRolePermissionRepository`. Conceptually cleaner separation of "fetch entities" from "resolve effective set." Rejected because the resolution is a single SQL query that wants to live next to the schema knowledge of the join shape. A service wrapper adds a layer for no real gain. If the resolution grows complex (caching, multi-step computation), a service layer can be introduced over the repository method without disturbing callers.

### Role hierarchy (Administrator > QM > Operator implicit promotion)

Rejected for the same reason as the prior ADR 0007 draft and reinforced by the permission model: hierarchy is unnecessary when permissions are explicit. An administrator who needs QM capability gets the QM role assigned (or gets the specific permissions granted directly). Hierarchy adds a global rule every authorization check has to reason about, in exchange for saving a few rows in the link table — bad trade.

## Consequences

### Positive

- Authorization checks are about what the code is doing (`Document.Approve`), not who the user is by title. Refactors that change job titles or org structure don't touch authorization code.
- Multi-hat users are modeled exactly as they are in real life: one user, multiple authorities, each separately auditable.
- Per-user grants give the "trained and certified for X" reality a first-class home in the data model. An auditor sees "this user was authorized to perform X from this date to this date, granted by this administrator."
- Effective dating on both link tables means historical compliance questions are answerable from the schema alone.
- The Administrator role's intended scope (IT-side, not operational) is enforced by the seeded permission set.
- `PermissionNames` constants make typos compile-time visible.

### Negative (and accepted)

- **More entities, more migrations, more audit rows.** Phase 1 bootstrap writes ~26 audit rows where F1 wrote 4. Correct (every grant is auditable) but worth seeing concretely once before assuming the audit log can hide.
- **No subtract-from-role mechanism yet.** A user with a role whose membership should temporarily not allow a specific action has to lose the role and re-acquire it later, or the role has to be split. Documented as a known limitation; a future ADR addresses if the case arises.
- **Permission catalog is committed to migrations forever.** A permission once seeded cannot be renamed without a migration that updates every link-table row. Accepted because the constraint enforces naming discipline up front.
- **Snapshot-at-sign-in means mid-session grants don't apply until re-login.** Unobservable today (no mid-session grant UI). When admin UI lands, a future ADR decides whether to switch to live re-resolution or keep snapshot with an explicit re-sign-in prompt.
- **Two repository round-trips at sign-in** (role names, then permission names). Both are indexed-key joins on a small table; the overhead is well under the PBKDF2 verify cost. If it ever matters, the two can be merged into a single multi-result query.

## Implementation Notes

- New domain entities (`EasySynQ.Domain`): `Permission`, `RolePermission`, `UserPermission`. `RolePermission` and `UserPermission` use the existing `EffectiveDateRange` owned type via a property named `EffectivePeriod`, mirroring `UserRole.EffectivePeriod`.
- New EF Core migration: creates the three tables; seeds the Phase 1 `Permission` rows; adds indexes on `RolePermission(RoleId, PermissionId)` and `UserPermission(UserId, PermissionId)`.
- `PermissionNames` static class in `EasySynQ.Domain`. Values match the seeded `Permission.Name` rows exactly.
- `IPermissionRepository` in `EasySynQ.Data`, registered in DI. Implementation issues the union query described above; per ADR 0002, services never see `EasySynQDbContext` directly.
- `IUserRoleRepository` in `EasySynQ.Data`, registered in DI. Single method `GetEffectiveRoleNamesAsync`.
- `IBootstrapService.CreateAdministratorAsync` extends to also write the RolePermission rows for the system permissions before the single `SaveChangesAsync`. The same `_clock.UtcNow` instant populates every `EffectiveFromUtc` written and is later passed to the role and permission lookup calls.
- `IAuthenticationService.AuthenticateAsync` happy path (after lockout-state reset, before `SaveChangesAsync`) captures `_clock.UtcNow` once; passes it to both `_userRoles.GetEffectiveRoleNamesAsync` and `_permissions.GetEffectivePermissionNamesForUserAsync`. Both results populate `AuthenticationResult.Success`.
- `ICurrentUserAccessor.SetCurrentUser` signature changes; mechanical refactor across both App handlers. Both stop passing the literal `"Authenticated User"`.
- `EventId 6001 LogSignInSucceeded` and `EventId 6004 LogBootstrapSucceeded` log emits include both `{Roles}` and `{Permissions}` as structured properties.
- SPEC §3.4 amendment ships in the same commit as the implementation; spec revision number bumps per CLAUDE.md Spec Drift rules.

## Required Tests

- `IPermissionRepository.GetEffectivePermissionNamesForUserAsync`:
  - User with one role that has three permissions → returns those three names.
  - User with one role and one direct grant → returns role permissions plus the direct grant, deduped.
  - User with one role that has a permission *also* granted directly → returns one entry, not two.
  - Expired RolePermission row excluded.
  - Future-effective RolePermission row excluded.
  - Expired UserPermission row excluded.
  - Future-effective UserPermission row excluded.
  - User with no role and no direct grants returns empty (not null).
- `IUserRoleRepository.GetEffectiveRoleNamesAsync`:
  - Multi-role user returns all current names.
  - Expired UserRole excluded; future-effective UserRole excluded.
  - No-role user returns empty.
- `IAuthenticationService` success path populates `AuthenticationResult.Success.Roles` and `.Permissions`.
- `IBootstrapService` success path populates `BootstrapSucceededEventArgs.Roles` with `["Administrator"]` and `.Permissions` with the eleven system permission names (any order).
- `IBootstrapService.CreateAdministratorAsync` writes the full set of RolePermission rows in one transaction with one CorrelationId; audit-row count matches the expected total of ~26.
- App handler regression: literal `"Authenticated User"` is absent from both `OnLoginSucceeded` and `OnBootstrapSucceeded` (asserted by inspection or grep).
- Smoke step: fresh install → bootstrap as Administrator → MainWindow opens → log file's `EventId 6004` entry shows `Roles=["Administrator"]` and `Permissions=[...the 11 system permissions...]` in structured fields. Sign out, sign back in → `EventId 6001` shows the same.

## References

- `docs/SPEC.md` §3.4 (Authorization — amended by this ADR), §3.7 (Effective dating — load-bearing for the link-table dating), §6 (Data Model — `User`, `Role`)
- ADR 0006 (authentication mechanics; bootstrap path extended by this ADR)
- ADR 0002 (audit-log invariant — repository pipeline)
- `docs/SESSION_NOTES.md` 2026-05-13 entry — Phase 1 Follow-Up #5 (resolved by this ADR)
