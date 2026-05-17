# ADR 0011 — Seeded Operational Roles for Document Controller

**Status:** Accepted
**Date:** 2026-05-16 (Proposed), 2026-05-16 (Accepted)
**Supersedes:** None
**Related:** ADR 0007 (permission model — operational roles bundle permissions, the spec does not prescribe role names); ADR 0008 (Phase 2 scope — rejected a seeded Author role; this ADR reverses that rejection, see §"Alternatives Considered > Make Author a seeded role" in 0008 for the original reasoning)

---

## Context

ADR 0008's seeded operational role for Phase 2 is `QualityManager`, configured with every Phase 2 document permission **except** `Document.AssignReviewers`. The omission was deliberate: the small-shop default ("any user who can author drafts can also submit them") and the strict-gatekeeper default ("only QM submits") were treated as equally valid policies that the seeded data should not pre-empt. Organizations were expected to grant `Document.AssignReviewers` after first run via admin UI — to author roles for the small-shop case or to QualityManager for the strict-gatekeeper case.

Two pieces of evidence accumulated during Phase 2 C6 surface a problem with that framing.

**Deployment-model clarification (2026-05-16).** The pilot deployment's intent is sharper than ADR 0008 anticipated. The `Administrator` role is the IT-side seat — it administers users/roles/permissions but does not author, submit, review, or sign documents. The deployment-default expectation is "users who can author drafts inherently submit them" — `Document.SubmitForReview` and `Document.AssignReviewers` both reach authors via their operational role. Strict-gatekeeper deployments (only QM submits / only QM assigns) are the configurable exception, not the default.

**C6b smoke-walk evidence (2026-05-16).** The C6b smoke walk could not run end-to-end without four direct `UserPermission` (or `UserRole`) grants written by a shell script:

1. Admin granted `Document.HardDelete` directly so the Draft hard-delete affordance renders.
2. Admin granted the four review-flow permissions (`Document.SubmitForReview`, `Document.AssignReviewers`, `Document.ReturnForEdits`, `Document.Review`) directly so the C6b author surfaces appear.
3. Admin granted membership in `QualityManager` so the role-prompter (ADR 0009) finds an eligible role to stamp on signatures.
4. The operational `multireviewer` test user granted `Document.AssignReviewers` directly — because `QualityManager`'s seeded permission set omits it.

All four are smoke-pragmatism workarounds: admin isn't operationally signing or hard-deleting documents in production, and the operational `multireviewer` is in `QualityManager` already — yet the seed shape makes them necessary to exercise the documented user-facing flows. The fourth case is the closest to a legitimate production grant, but it works around a real gap: on a fresh-bootstrap install, **no user holds `Document.AssignReviewers` at all**, and the supposed small-shop default ("author-can-submit") is not the default — it requires admin-UI grants that have not been built yet.

The C6b walk caught the gap, but the diagnostic frame is generalizable: when the seeded data doesn't match the documented deployment-model intent, every smoke walk has to paper over the gap, and every smoke run encounters the same friction afresh. A real production deployment would face the same friction at first install.

This ADR reconciles the seed shape with the deployment-model intent. The same commit that ships the new migration amends SPEC §5.1's Authorization paragraph (per CLAUDE.md Spec Drift rules — the spec currently asserts `Document.AssignReviewers` is not assigned to `QualityManager` by default, which this ADR changes).

## Decision

### Two seeded operational roles

The Phase 2 migration chain seeds two operational roles, not one. Together they realize the deployment-model intent out of the box: a fresh install supports the author-can-submit small-shop workflow with no admin-UI intervention, and the strict-gatekeeper variant is reachable by revocation rather than by construction.

**`DocumentAuthor` (new).** A generic operational role for users whose primary responsibility is authoring controlled documents. Permission set:

| Permission | Reason |
|---|---|
| `Document.Create` | Author creates new internal documents. |
| `Document.EditDraft` | Author edits their Draft revisions. |
| `Document.HardDelete` | Author hard-deletes Drafts they no longer want (per SPEC §3.5, Drafts carry no signatures and are author-owned). |
| `Document.SubmitForReview` | Author submits their Draft for review. |
| `Document.AssignReviewers` | Author names the reviewer set when submitting (the small-shop "author-can-submit" affordance). |

The role's name is generic deliberately — organizations whose actual authoring users are "Production Manager", "Maintenance Manager", "Quality Engineer", etc. assign their users to `DocumentAuthor` directly, rename it, or build their own author-role equivalent via admin UI. The spec does not prescribe authoring terminology (ADR 0007 §"Roles are admin-defined named bundles"); the seed provides a name that works without commentary.

**`QualityManager` (amended).** Gains the previously-omitted `Document.AssignReviewers` permission. Its full permission set is now every Phase 2 document permission — 13 rows in `RolePermission` instead of the prior 12. Reasoning: a `QualityManager` user is empowered to handle every step of the document lifecycle. The strict-gatekeeper-only-QM-submits policy is reached by revoking `Document.Create`, `Document.EditDraft`, `Document.SubmitForReview` (and other authoring permissions) from `DocumentAuthor` after first run, or by leaving `DocumentAuthor` unassigned to operational users. Either path is admin-side configuration, not a re-keying of the seeded role.

### Migration shape

A single migration named `AddDocumentAuthorRoleAndAmendQualityManagerSeed` lands the changes:

- One `Role` row inserted: `DocumentAuthor` with a deterministic `Id` (prefix `08100000-`, slot 02 — slot 01 is `QualityManager`).
- Five `RolePermission` rows inserted linking `DocumentAuthor` to its five permissions, with deterministic `Id`s in a new prefix (`08210000-`).
- One `RolePermission` row inserted linking `QualityManager` to `Document.AssignReviewers`, filling the deliberately-skipped slot 04 of the existing `08200000-` prefix (the C1 migration left slot 04 open precisely because it skipped this link — the slot now gets used).
- `EffectivePeriod` values follow ADR 0007's bootstrap pattern: `EffectiveFromUtc` captured once at migration apply time via `DateTime.UtcNow`, `EffectiveToUtc` null (open-ended).
- Migration-time inserts bypass the audit interceptor per ADR 0007 precedent. The migration's git history is its audit trail; the rows are attributed to `CreatedBy = "system:migration"` as the prior catalog migrations do.

The migration is straightforward `InsertData` — no upgrade-path conditionals are needed. The C1 migration that seeded the `QualityManager` role and the Phase 2 permission catalog runs strictly before this one, so by the time this migration applies, the rows it depends on (the `QualityManager` role row, the 13 Phase 2 permission rows) exist on every database. There is no equivalent of the F1-era "legacy administrator with no permissions" upgrade case for `QualityManager` because `QualityManager` has only ever existed as a migration-seeded row.

### `PermissionNames.QualityManagerDefaults` updated; new constant for `DocumentAuthor`

The code-side mirror in `EasySynQ.Domain.PermissionNames` updates to match the new seed shape:

- `QualityManagerDefaults` (existing static readonly list) gains `DocumentAssignReviewers` — its content is now equal to `Phase2Document`. The constant's purpose is unchanged: it names the permissions the migration-seeded `QualityManager` role holds.
- New static readonly list `DocumentAuthorDefaults` enumerates the five `DocumentAuthor` permissions in the order the migration seeds them. Its purpose is the same as `QualityManagerDefaults` — to support the migration-seed test's exact-match assertion against `PermissionNames`.

The migration-seed test (`DocumentControllerMigrationSeedTests`) is updated to:

- Invert the prior `QualityManagerRole_DoesNotHaveDocumentAssignReviewersAsync` assertion. The new test asserts `QualityManagerRole_HasDocumentAssignReviewers` — load-bearing for the reversal; if a future migration removes the grant, this test fires.
- Continue to assert `QualityManagerRolePermissions_MatchQualityManagerDefaultsExactlyAsync` (the list contents change; the assertion shape doesn't).
- Add `DocumentAuthorRole_ExistsWithDeterministicIdAsync` mirroring the existing `QualityManagerRole_ExistsWithDeterministicIdAsync`.
- Add `DocumentAuthorRolePermissions_MatchDocumentAuthorDefaultsExactlyAsync` mirroring the QM equivalent.
- Add open-ended and `system:migration` attribution checks for `DocumentAuthor` rows.

A new test in the same fixture pins the migration-applies-cleanly invariant for a database that already had the C1 migration applied: applying this migration on top adds exactly six new `RolePermission` rows (five for `DocumentAuthor` plus one for the `QualityManager` amendment) and one new `Role` row, with no duplicates.

### Fixture-level integration test updates

A handful of integration tests grant document permissions to test users by inserting `UserPermission` rows directly (the data-layer base-class pattern). The new seed reduces the need for these grants in two cases:

- **QM-roled test users.** Tests that grant `Document.AssignReviewers` directly to a user who is also in `QualityManager` (because their downstream assertion requires effective `AssignReviewers`) no longer need the direct grant — the role now provides it. The direct-grant lines are removed; the assertion holds via the union resolution.
- **Non-QM operational test users.** Tests that stack a handful of `Document.*` direct grants to model an "author-like" user assign the user to `DocumentAuthor` instead. This is the right shape forward — the test no longer has to enumerate the author permission set explicitly.

Tests that exercise the **unauthorized path** (assert the operation throws when a permission is absent) keep their current shape — they explicitly do NOT grant the permission in question. The new roles do not change that pattern.

Tests that exercise the **permission resolution algorithm** (`PermissionRepositoryTests`, etc.) keep their direct `UserPermission` inserts as-is — those tests are about the algorithm, not about modeling a deployment-shaped user, and per-row coverage is the goal.

### Smoke script audit

`scripts/grant-document-permissions.ps1` updates in the same chunk of work but as a separate commit (per the plan's three-commit shape). Specifically:

- The four direct grants the script currently writes are removed:
  - admin's `Document.HardDelete` direct grant.
  - admin's four-permission direct grant (`SubmitForReview` + `AssignReviewers` + `ReturnForEdits` + `Review`).
  - the admin → `QualityManager` `UserRole` row.
  - `multireviewer`'s direct `Document.AssignReviewers` grant.
- The smoke procedure (comments + walkthrough doc) updates to walk through with a `DocumentAuthor`-roled test user for author-side gestures (Create, EditDraft, HardDelete, SubmitForReview, AssignReviewers) and a `QualityManager`-roled test user for review-side gestures (Review, ReturnForEdits, Retire). Admin is not used for any operational gesture — matching the deployment-model intent.
- The script retains the `multireviewer`/`secondreviewer` test-user mint logic, the `ReviewerSecondary` role mint, the role memberships needed to exercise the multi-role `SignAsRoleDialog` path, and the effective-permission pre-flight verification helper. These are genuine test-data (not seed-gap workarounds) and stay.

After the script audit lands, the C6b smoke walk re-runs as the clean test of the new seed: a fresh-bootstrap install plus the (now slimmer) smoke script must complete the C6a + C6b walk without `permission denied` errors. If it doesn't, the seed shape is incorrect and the failure surfaces concretely (which permission, which gesture, which user).

### SPEC §5.1 amendment

Same commit that ships the migration amends SPEC §5.1's Authorization paragraph from:

> A default `QualityManager` role is seeded with all Document and ExternalDocument permissions assigned (organizations can modify via admin UI when it ships). `Document.AssignReviewers` is intentionally not assigned to QualityManager by default — organizations grant it to author roles for the small-shop default or restrict to QualityManager for the strict-gatekeeper model.

to:

> Two operational roles are seeded by Phase 2's migration chain. **`DocumentAuthor`** — a generic author role granting `Document.Create`, `Document.EditDraft`, `Document.HardDelete`, `Document.SubmitForReview`, and `Document.AssignReviewers` — supports the small-shop default where users who can author drafts can also submit them. **`QualityManager`** — granting every Phase 2 document permission, including `Document.AssignReviewers` — supports both the cross-functional reviewer role and the strict-gatekeeper variant where authoring permissions are revoked from `DocumentAuthor` after first run. Organizations can rename, modify, or remove either seeded role via admin UI when it ships; the seed shape is a starting point, not a constraint.

SPEC revision bumps from 3.4 to 3.5 with a row in the Revision History table describing the §5.1 amendment.

## Alternatives Considered

### Path A standalone — amend `QualityManager` seed only, no new role

The smallest possible reconciliation: add `Document.AssignReviewers` to `QualityManager`'s seeded permissions and stop there. Any operational user becomes a `QualityManager` member, and the small-shop default is reached by membership rather than by a role bundle.

Rejected because it conflates two distinct deployment-model roles into one. Real shops differentiate between "the author who writes the procedure" and "the QM who approves it" — both should be able to submit-for-review under the small-shop default, but the QM role carries review responsibility (and the `Document.Review` permission) that the author role should not. Conflating them means every author user is also a reviewer user, which makes the assigned-reviewer model (ADR 0008 §"Assigned-reviewer model") harder to police — an author could appoint themselves as the reviewer (a gap that ADR 0008's C3 guard catches at the service layer, but only by guarding *every* submit). Two roles with different permission sets is the cleaner deployment-model fit.

### Path B standalone — seed `DocumentAuthor` only, leave `QualityManager` unchanged

Mirror image of Path A. Add the new `DocumentAuthor` role; do not amend `QualityManager`. The author-can-submit small-shop default works because `DocumentAuthor` holds `Document.AssignReviewers`; `QualityManager` continues to omit it.

Rejected because it leaves the `QualityManager` permission set incomplete for the use case where a QM user also submits for review. A QM user authoring a quality manual would have to be doubly-roled (`QualityManager` + `DocumentAuthor`) to submit it — every signing operation would then hit the `SignAsRoleDialog` role picker (ADR 0009), and the audit trail would be ambiguous about which role they were acting under for each signature step. Granting `QualityManager` the full Phase 2 permission set avoids the ambiguity in the common case.

### Path C — bootstrap-time `--strict-gatekeeper` flag

Keep the spec-described behavior available as a deployment-time choice: a bootstrap-flag (or first-run-prompt) selects between "small-shop default" (`DocumentAuthor` seeded, `QualityManager` includes `AssignReviewers`) and "strict-gatekeeper" (no `DocumentAuthor`, `QualityManager` omits `AssignReviewers`).

Rejected because the flag adds a configuration surface that the codebase has no precedent for. Bootstrap is currently a single-decision-point operation (ADR 0006 — create the administrator). Adding a policy-shaping option to it introduces a bootstrap-time deployment fork that admin UI cannot later collapse cleanly — the rows present in the database differ between the two paths. The same goal is reachable by post-install configuration (revoke `Document.Create` and friends from `DocumentAuthor`, leave `QualityManager` as the only role with submit authority) without new infrastructure. The seed should be a starting point that admin UI can edit, not a forked schema.

### Status-quo — leave the seed as ADR 0008 specified

Continue requiring organizations to grant `Document.AssignReviewers` via admin UI after first run. Smoke walks continue using the script's direct grants until admin UI ships.

Rejected because the deployment-model clarification means the documented "small-shop default" is what the seed should produce out of the box, not what an admin must configure into existence on first run. The C6b smoke evidence shows the friction concretely — admin UI is still a phase or two away, and every smoke walk in the interim re-papers over the same gap. The cost of seeding two roles correctly is one migration; the cost of leaving the gap is friction at every smoke and at every fresh deployment until admin UI lands.

## Consequences

### Positive

- The deployment-model intent (author-can-submit, admin-is-not-operational) is the out-of-the-box default. Organizations install, bootstrap, sign in as administrator, assign users to roles, and the documented workflows run without further configuration.
- Smoke walks run against the seeded roles directly, not against scripted direct grants. The script's surface shrinks to genuine test-data (test-user mints + the multi-role `multireviewer` for the `SignAsRoleDialog` path). The "smoke pragmatism" workarounds that papered over the gap (catalogued in the SCRATCHPAD) all disappear.
- The two-role split mirrors how real users think about their responsibilities — authoring and reviewing are distinct activities, modeled as distinct roles. Users in only one of the two see only their relevant permissions and affordances.
- The strict-gatekeeper deployment shape is reachable from the seeded starting point by **revocation**, not by **construction**. Admin removes authoring permissions from `DocumentAuthor` (or leaves it unassigned). The seeded data does not pre-empt the policy; it provides a default that admin can edit.
- The `Document.AssignReviewers`-via-direct-`UserPermission` corner case (SCRATCHPAD entry "ADR 0009 role-resolution vs direct UserPermission grants") becomes practically rare. In the seeded shape, every user with `Document.AssignReviewers` holds it through a role membership, so the role-prompter has an eligible role to surface. The corner case still exists architecturally (a deployment could still grant `Document.AssignReviewers` via direct `UserPermission` from admin UI in some future configuration) and is deferred to its own ADR — but the C6b smoke can no longer drive into it.

### Negative (and accepted)

- **Strict-gatekeeper deployments require admin-side revocation.** Organizations that wanted the prior ADR 0008 default (only QM can submit, authors have to ask a QM) must now revoke authoring permissions from `DocumentAuthor` after first run, or leave `DocumentAuthor` unassigned to their users. This is more steps than the prior default (which was zero steps for strict-gatekeeper but four direct grants for small-shop). The change favors the more-common deployment shape.
- **Earlier pressure on admin UI for role management.** Once `DocumentAuthor` and `QualityManager` are seeded with concrete permission sets, organizations will want to edit them — rename `DocumentAuthor`, add `Document.Review` to a "Senior Author" variant, etc. Admin UI for role and permission management was already on the post-Phase-2 roadmap (per ADR 0008 §"Out of scope for Phase 2"); this ADR amplifies the pull. The interim mitigation is the same as today — direct DB manipulation (or a more focused script than the smoke script became) — and the seed shape works for the pilot deployment as-is, so the pressure is on the deployment-fitness margin rather than on a blocking dependency.
- **`PermissionNames.QualityManagerDefaults` changes meaning.** The constant still names "permissions granted to the seeded QualityManager role"; what changes is that the list is now equal to `Phase2Document` (13 entries) instead of the prior 12. Any code that compared `QualityManagerDefaults` and `Phase2Document` as a *distinct* pair would silently misbehave; the migration-seed test pins both lists explicitly so the equality is asserted, not assumed.
- **The migration is the third seed-affecting Phase 2 migration after C1.** C1 seeded the catalog + `QualityManager`; the Vault permission migration added `Vault.PhysicalDelete`; this one adds `DocumentAuthor` + amends `QualityManager`. The migration list is starting to fragment; future-Phase migrations should consider whether they can land their seed amendments together rather than one-per-chunk. No rule, just a watch-item for the next phase planner.
- **Existing installs (none in production, but worth noting for forward consistency).** The migration applies cleanly to any database that has C1 applied. A hypothetical pre-existing install would get `DocumentAuthor` added and the new `QualityManager` link row written. Existing `UserRole`/`UserPermission` rows are untouched. No data loss; no role-membership changes; effective permission sets grow but never shrink. Acceptable.

## Implementation Notes

- Migration name: `AddDocumentAuthorRoleAndAmendQualityManagerSeed`.
- New `Role.Id`: `08100000-0000-0000-0000-000000000002` for `DocumentAuthor`.
- New `RolePermission.Id` prefix for `DocumentAuthor` link rows: `08210000-`. Suffixes follow the C1 convention of matching the permission's own suffix (`01` for `Document.Create`, `02` for `Document.EditDraft`, etc.) so visual tracing across raw queries is straightforward.
- New `RolePermission.Id` for the `QualityManager → Document.AssignReviewers` link row: `08200000-0000-0000-0000-000000000004` (fills the deliberately-skipped slot 04 of the C1 migration's `QualityManager` prefix).
- `PermissionNames.QualityManagerDefaults` updated to include `DocumentAssignReviewers`. The list ordering matches the migration's `RolePermission` insert order to keep the test's `BeEquivalentTo` assertion shape stable across review.
- New `PermissionNames.DocumentAuthorDefaults` static readonly list — same shape as `QualityManagerDefaults`.
- Existing test `QualityManagerRole_DoesNotHaveDocumentAssignReviewersAsync` is removed and replaced by an inverted `QualityManagerRole_HasDocumentAssignReviewers` assertion (a load-bearing reversal — the new test pins the new behavior; deleting without replacing would lose the regression guard).
- New tests added to `DocumentControllerMigrationSeedTests`:
  - `DocumentAuthorRole_ExistsWithDeterministicIdAsync`.
  - `DocumentAuthorRolePermissions_MatchDocumentAuthorDefaultsExactlyAsync`.
  - `DocumentAuthorRolePermissions_AreAllOpenEndedAsync`.
  - `DocumentAuthorRolePermissions_HaveSystemMigrationAttributionAsync`.
  - A new test class or fixture file `AddDocumentAuthorRoleAndAmendQualityManagerSeedMigrationTests` (or a `[Fact]` in the existing file) asserts the row-delta invariant for an install path that has C1 applied then applies this migration: exactly one new `Role` row, exactly six new `RolePermission` rows, no duplicates, no rewritten existing rows.
- Integration-test fixture updates are scoped to tests that grant Phase 2 document permissions to test users via direct `UserPermission` inserts AND whose semantic intent is "model a deployment-shaped user, not exercise the permission-resolution algorithm." Targets: a small number of rows in `AuthenticationServiceTests`, `UserRepositoryTests`, and `BootstrapServiceTests`. Tests in `PermissionRepositoryTests` and `DocumentReviewCommentRepositoryTests` whose intent is the algorithm or the repository's own contract are left alone.
- `BootstrapServiceTests`'s comment at the QualityManager scoping assertion (the "12 of them, one per Phase 2 document permission except Document.AssignReviewers" comment) updates to "13 of them, the full Phase 2 document permission set."
- SPEC revision bumps from 3.4 to 3.5; revision-history row added describing the §5.1 amendment.
- The migration commits as a three-commit chunk per the plan: (1) ADR 0011 + ADR 0008 cross-reference, (2) migration + tests + fixture updates + SPEC amendment, (3) smoke-script audit. Each commit stops for review per CLAUDE.md commit authorization.

## Required Tests

- `DocumentControllerMigrationSeedTests` (existing) — updates as above:
  - `QualityManagerRolePermissions_MatchQualityManagerDefaultsExactlyAsync` (existing, asserts the updated 13-entry list).
  - `QualityManagerRole_HasDocumentAssignReviewersAsync` (new, inverted from the prior negative assertion).
  - `DocumentAuthorRole_ExistsWithDeterministicIdAsync` (new).
  - `DocumentAuthorRolePermissions_MatchDocumentAuthorDefaultsExactlyAsync` (new).
  - `DocumentAuthorRolePermissions_AreAllOpenEndedAsync` (new).
  - `DocumentAuthorRolePermissions_HaveSystemMigrationAttributionAsync` (new).
- `AddDocumentAuthorRoleAndAmendQualityManagerSeedMigrationTests` (new test file or `[Fact]` in the existing seed-tests file): applying the migration on a C1-applied database produces exactly one new `Role` row (`DocumentAuthor`) and exactly six new `RolePermission` rows (five `DocumentAuthor` links + one new `QualityManager → AssignReviewers` link), with no duplicate rows and no row count changes for any other entity.
- `BootstrapServiceTests` — the existing assertion on Administrator-scoped `RolePermission` count is unchanged (scoped to the Administrator role). The comment about QualityManager's scoping count updates to reflect 13 rows. No new test required; existing pins remain valid.
- Integration tests touched by the fixture updates pass with the new seeded roles in place — five-run stress test per CLAUDE.md test-stability policy.
- Smoke walk (C6b, manual) re-runs against the new seed with the script's direct UserPermission grants removed and the smoke procedure updated to use `DocumentAuthor` / `QualityManager` test users. The walk completes the full C6a + C6b gesture set without `permission denied` errors.

## References

- `docs/SPEC.md` §5.1 (Document Controller — Authorization paragraph amended by this ADR), §3.4 (Authorization — unchanged), §9 (Roadmap)
- ADR 0007 (permission model — operational roles are admin-defined bundles; this ADR adds a second seeded operational role under that pattern)
- ADR 0008 (Phase 2 scope — §"Alternatives Considered > Make Author a seeded role" rejected this ADR's decision; the cross-reference appended to that section documents the reversal)
- ADR 0009 (signature dialog UX — the role-resolution corner case the C6b smoke surfaced is related but architecturally distinct; the seed change reduces practical exposure but does not resolve the corner case)
- `docs/SCRATCHPAD.md` "Smoke-script grants paper over a seed-shape gap" (lesson pinned C6b stop 9; the architectural follow-up this ADR realizes)
- `docs/SCRATCHPAD.md` "ADR 0009 role-resolution vs direct UserPermission grants" (the corner case this seed change makes practically rare; left to its own future ADR)
- `docs/SESSION_NOTES.md` 2026-05-16 (Phase 2 C6b) — the smoke-walk evidence that motivated this ADR
