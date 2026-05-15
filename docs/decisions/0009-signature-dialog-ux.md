# ADR 0009 — Signature Dialog UX (Sign-As-Which-Role)

**Status:** Accepted
**Date:** 2026-05-15 (Proposed), 2026-05-15 (Accepted)
**Supersedes:** None
**Related:** ADR 0007 (permission model — the role-filtering decision below builds on the permission catalog); ADR 0008 (Phase 2 scope — C3 deferred this ADR; this commit pairs with C4)

---

## Context

ADR 0007 established that users hold a *set* of roles (`ICurrentUserAccessor.Roles`) rather than a single resolved role, and that authorization decisions check permissions, not role names. But signatures per SPEC §3.4 capture "role-at-time-of-sign" as a single string on the Signature row. The two models meet at the signing operation: a user with one role signs unambiguously as that role, but a user with multiple roles needs to choose which one applies to this specific signature.

The current SignatureService implementation handles this with a deliberate fail-fast: when `_currentUser.Roles.Count != 1`, it throws `InvalidOperationException` with a message naming the unresolved-role-picker as a Phase 1 follow-up. Phase 2 C3 added the first multi-entity signing flow (DocumentLifecycleService.SubmitForReviewAsync, SignAsReviewerAsync, RetireAsync) but did not encounter the throw because all C3 tests use single-role users. The first real multi-role signing surface lands in Phase 2 C6's UI shell.

This ADR specifies the dialog UX, defines the contract changes to SignatureService, and pairs with C4's initial dialog scaffolding. C4 ships the dialog itself; C6 wires it into actual user-facing signing flows.

The deployment context shapes the design. The pilot deployment has users like the Production Manager who holds both their Production Manager role and a QualityManager role (granted for QMS work), and a Lead Operator who is also an internal auditor. These users routinely act under different authorities for different actions: the Production Manager approves quality documents *as Quality Manager*, not as Production Manager. The signature row must capture that distinction, and the UI must make the choice deliberate.

## Decision

### Multi-role users see a role picker before signing; single-role users do not

When a user invokes a signing operation:

- **Zero roles:** `SignatureService` throws `InvalidOperationException` (no change from current behavior). A user with no roles attempting to sign is an error state, not a UX state.
- **Exactly one role:** signing proceeds with that role captured automatically. No dialog appears.
- **Two or more roles:** the UI layer presents a role picker dialog. The user selects which role they are acting as for this signature. The choice is passed explicitly to `SignatureService`.

This makes the multi-role case visible and deliberate without imposing friction on single-role users (who are the majority by deployment count). Single-role users see no change in behavior from before C4.

### Role picker is filtered by the permission required for the action

The dialog does not show all of the user's roles. It shows only roles that hold the permission gating the action being signed. Concretely:

- A user with `Administrator` + `QualityManager` approving a document (action requires `Document.Approve`): only `QualityManager` appears, because the seeded `Administrator` role does not hold `Document.Approve` (ADR 0007's deliberate separation of IT-side from operational permissions).
- A user with `QualityManager` + `MaintenanceManager` reviewing a document (action requires `Document.Review`): both roles appear if both hold `Document.Review`. The user picks the contextually correct one.
- A user whose every role holds the gating permission: all roles appear; user picks.
- A user whose no role holds the gating permission: the permission check at the lifecycle-service layer fails before signing is attempted; the dialog never appears. The user sees an authorization error, not a role picker.

The filter source is the permission model itself — the same catalog the lifecycle service uses to gate action invocation. No new metadata, no new registry; the existing permission model is reused.

Reasoning: this prevents the nonsensical "signed as Administrator on a document approval" outcome while requiring no new infrastructure. The audit row's `RoleAtTimeOfSign` value will always be a role that actually held the relevant permission at the time, which matches what an external assessor would expect when reviewing the signature.

### The choice is per-signature, not persisted across signatures

Each signing operation prompts the user fresh. A multi-role user signing 12 documents in a session picks their role 12 times. There is no "remember for this session" affordance in C4.

Reasoning: audit clarity. A persisted choice introduces ambient state — "user X signed as Y" becomes ambiguous about whether Y was a deliberate choice for *this* signature or carried over from an earlier choice. The per-signature pick eliminates the ambiguity at the cost of repeated clicks.

If real users complain of friction during pilot use, a "remember for this session" checkbox could land as a future ADR amendment with explicit audit-trail handling (the persisted choice would need its own audit-row semantics: the choice itself becomes a logged decision, not ambient state). The conservative default ships first.

### Cancellation aborts the signing operation

If the user closes the dialog or clicks Cancel, the signing operation aborts. No signature is staged, no state changes, no audit rows. The flow surfaces this as `OperationCanceledException` (composes naturally with the `CancellationToken` already flowing through the signing methods).

UI surfaces above the signing call catch the cancellation and return the user to the pre-signing state (the In Review document remains in In Review, the document being retired remains un-retired, etc.). No special handling required beyond catching the exception type.

### Contract change: signing methods take an explicit role parameter

`SignatureService.SignAsync` and `StageSignatureAsync` gain a new parameter `string signingAsRole`. Callers pass the role the user is signing as, which becomes the `RoleAtTimeOfSign` value on the persisted Signature.

The previous behavior (read `_currentUser.Roles.Single()` and use that) is removed entirely. There is no fallback — if the caller doesn't pass an explicit role, the code doesn't compile. This forces every signing call site to handle the multi-role case deliberately.

Validation in the service: the passed role must be a member of the current user's effective roles (`_currentUser.Roles.Contains(signingAsRole)`). If not, throw `InvalidOperationException` — this catches programming errors where the UI passes a role the user doesn't actually hold.

### Dialog lives in the UI project; service layer stays UI-free

The dialog (View + ViewModel) ships in `EasySynQ.UI`. The signature service in `EasySynQ.Services` does not know the dialog exists. UI signing flows are responsible for:

1. Determining the permission required for the action being signed (from the lifecycle service's known gating permissions — `PermissionNames.DocumentReview`, etc.).
2. Filtering the current user's roles by that permission.
3. If exactly one role remains, calling the signature service directly with that role.
4. If multiple roles remain, presenting the picker dialog, awaiting the user's choice, and calling the signature service with the chosen role.
5. If zero roles remain (a defensive case — the lifecycle service's permission check should have already failed), throwing a clear error.

The role-filtering helper lives in `EasySynQ.UI` (or in `EasySynQ.Services.Authorization` if reused across UI surfaces) — probably the latter, since the same filtering logic applies to any signing surface.

### What ships in C4

The C4 commit lands:

- New service `IRoleResolutionService` (or similarly-named) in `EasySynQ.Services.Authorization` with one method `GetEligibleRolesForPermission(string permissionName)` returning the current user's roles that hold the given permission. Pure synchronous helper over `ICurrentUserAccessor.Roles` plus a permission-to-roles lookup (which itself queries the permission-role link tables — sees current effective rows, same as the resolution at sign-in).
- New WPF dialog `SignAsRoleDialog` and `SignAsRoleViewModel` in `EasySynQ.UI`. The dialog displays the eligible roles as radio buttons (or a list if >5; under 5 the radio button list is more scannable) with an OK and Cancel button.
- Contract change to `ISignatureService.SignAsync` and `StageSignatureAsync`: new `signingAsRole` parameter. All existing call sites in C3's lifecycle service updated to pass the resolved role.
- UI signing-flow helper that wraps "filter → check count → dialog if needed → call service with chosen role" into one reusable call. The helper raises `OperationCanceledException` on dialog cancel.

What does **not** ship in C4:

- Actual user-facing signing flows (those land in C6 with the document detail view's submit-for-review and review-and-sign dialogs).
- Tests of the dialog in real user flows (covered by C6's UI smoke).
- The "remember for this session" affordance (future ADR amendment if needed).

### What this means for existing code

- C3's `DocumentLifecycleService` methods (`SubmitForReviewAsync`, `SignAsReviewerAsync`, `RetireAsync`) currently call `_signatures.StageSignatureAsync(...)` with no explicit role. They will gain a `signingAsRole` parameter that gets forwarded to `StageSignatureAsync`. Callers of the lifecycle service (today: only tests; in C6: the UI) are responsible for resolving the role before calling.
- C3's tests use single-role users (the seeded bootstrap Administrator). They will pass `"Administrator"` literally as the signing role. No test logic changes beyond the parameter addition.
- The `_currentUser.Roles.Single()` throw in SignatureService is removed.

## Alternatives Considered

### Show all roles, no filtering

Simpler UI logic. Rejected because it admits the nonsensical pick (Administrator signing a document approval). The filter prevents a class of audit-confusion at minimal cost.

### Action-type metadata registry mapping action → eligible roles

A registry maps `"DocumentApproval"` → `["QualityManager", "QualityLead"]`. The dialog reads from the registry. Rejected because it adds new infrastructure (a registry, its seeding, its tests) when the existing permission model already encodes the information. Filter-by-permission is the cheaper consistent shape.

### Persist the choice for the session

A "remember for this session" checkbox or default-from-last-pick. Rejected for the audit-clarity reason above. Available as a future amendment if real-user pressure justifies the audit complexity.

### Make signing imply a choice based on a documented precedence

A hardcoded "primary role" ordering (Administrator > QualityManager > ...) used to pick automatically. Rejected for the same reason ADR 0007 rejected precedence-based role resolution: it invents semantics the spec does not define, and is silently wrong in the cases that matter (an Administrator who *is* doing operational QM work would have their action attributed to Administrator).

### Throw on multi-role users, force admins to "fix" the data

Refuse multi-role users entirely; admins must redesign roles so no user holds more than one. Rejected because the multi-role case is the *deployment reality*, not a data anomaly. Forcing it out of existence means forcing real workflows (the Production Manager who runs QMS) into uncomfortable shapes.

## Consequences

### Positive

- Multi-role signing is deliberate. Audit rows reflect the role the user actually intended to act under, not a guess.
- The permission-filter reuses existing model. No new infrastructure for the picker beyond the dialog itself.
- Single-role users (the majority by deployment count) see no UX change.
- The contract change forces every signing call site to handle the multi-role case explicitly. There is no path where multi-role signing happens silently.
- The dialog's UI logic is contained in the UI project; the service layer stays UI-free.

### Negative (and accepted)

- **Multi-role users sign more clicks per session.** Pick-per-signature is friction. Accepted as the cost of audit clarity. Future amendment can add a per-session memo if real users complain.
- **Signing methods grow a parameter.** Every existing call site updated. Mechanical change but real diff.
- **The role-filter helper needs its own tests.** Eligible-roles-for-permission is a small surface but a real one; tests pin the filtering correctness across role/permission combinations.
- **The dialog needs its own UI tests.** Selecting OK with a chosen role returns it; Cancel raises `OperationCanceledException`; >5 roles renders correctly; etc.
- **Multi-role users where no role holds the gating permission see an authorization error rather than a "you can't do this with any of your roles" message.** The lifecycle service's permission check fires first because it checks `_currentUser.Permissions.Contains(...)` which is the union over all roles. If that passes, at least one role must hold the permission, so the dialog will always have ≥1 option. The dialog never shows an empty list.

## Implementation Notes

- `IRoleResolutionService` in `EasySynQ.Services.Authorization` (joins the existing `UnauthorizedOperationException`). Single method: `IReadOnlyList<string> GetEligibleRolesForPermission(string permissionName)`. Synchronous because it operates over `ICurrentUserAccessor.Roles` and a cached permission-role map (no DB roundtrip per call).
- The permission-role map is sourced from the same data the auth service used at sign-in. Two options for fetching it: (a) extend `ICurrentUserAccessor` to carry the role-to-permissions map directly (in addition to the flat permissions list); (b) query the `IPermissionRepository` on each `GetEligibleRolesForPermission` call. (a) is faster and matches the snapshot-at-sign-in pattern; (b) is consistent with the "no derived state on the accessor" minimalism. Lean: (a) — extend the accessor's signature snapshot to include the per-role breakdown.
- `SignAsRoleDialog` follows existing WPF dialog conventions in the project (modal, OwnedBy MainWindow when applicable). The dialog ViewModel exposes `Roles : IReadOnlyList<string>`, `SelectedRole : string?`, `OkCommand`, `CancelCommand`.
- `OperationCanceledException` on dialog cancel composes with `CancellationToken` flow already in place. Callers above the signing call catch it (or let it propagate to the UI layer's standard cancellation handling).
- The contract change to SignatureService is mechanical: existing single-role callers pass `_currentUser.Roles.Single()` literally; UI multi-role callers pass the dialog's chosen value. No new method, no new overload — just a new required parameter.

## Required Tests

### Unit tests (no DB, fast)

- `IRoleResolutionService.GetEligibleRolesForPermission`:
  - User with one role that holds the permission returns that role.
  - User with one role that doesn't hold the permission returns empty.
  - User with two roles, only one holds the permission, returns the one.
  - User with two roles both holding the permission returns both.
  - User with zero roles returns empty.
  - Permission name that doesn't exist in catalog returns empty (not throw — the user simply has no eligible role).
- `SignAsRoleViewModel`:
  - Constructed with a list of roles, exposes them in order.
  - SelectedRole starts null; OkCommand can-execute is false until a selection is made.
  - SelectedRole set, OkCommand can-execute is true.
  - CancelCommand always can-execute.

### Integration tests

- `SignatureService.SignAsync` and `StageSignatureAsync` with a valid `signingAsRole` (a role the user holds) succeed; the persisted Signature row has the correct `RoleAtTimeOfSign`.
- `SignatureService.SignAsync` with an invalid `signingAsRole` (a role the user does not hold) throws `InvalidOperationException`.
- `SignatureService.SignAsync` with a zero-role user (`_currentUser.Roles` is empty) throws `InvalidOperationException`. (This case never reaches signing under normal flow but the defensive throw stays.)

### Manual smoke (C4)

- Create a multi-role test user with Administrator + QualityManager roles via direct DB manipulation (admin UI for role assignment lands later).
- Sign in as that user.
- Confirm the dialog never appears in C4 itself (no user-facing flow invokes it yet — the dialog scaffolding exists but isn't wired). This is expected; smoke for the actual dialog interaction lands with C6.

The C4 commit is forward-looking; the real-user-facing test of the dialog happens in C6 when document workflows actually trigger signing.

## References

- `docs/SPEC.md` §3.4 (Authorization — permission-based; signatures capture role-at-time-of-sign)
- ADR 0007 (permission model — IT-side vs operational role separation; permissions as the unit of authorization)
- ADR 0008 (Phase 2 scope — C4 paired with this ADR per the chunking)
- `docs/SESSION_NOTES.md` 2026-05-14 (Phase 2 C3) — handoff notes that this ADR pairs with C4
