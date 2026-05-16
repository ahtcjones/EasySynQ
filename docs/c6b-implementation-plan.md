# Phase 2 C6b — Implementation Plan (agreed)

Authoritative plan document for the C6b commit. Produced in a planning chat session that consumed the full reading list and converged on the open questions. Execute against this plan; flag any scope-creep at the named stop points per CLAUDE.md convention.

---

## Reading consumed during planning

- CLAUDE.md (working rules, non-negotiables, phase order, commit authorization, test stability protocol)
- docs/SPEC.md §3.4, §3.5, §3.7, §4.3, §5.1
- docs/decisions/0002-hard-delete-audit-log.md
- docs/decisions/0007-permission-based-authorization-model.md
- docs/decisions/0008-phase-2-document-controller-scope.md
- docs/decisions/0009-signature-dialog-ux.md
- docs/decisions/0010-pdf-viewer-webview2-pdfjs.md (incl. 2026-05-16 amendments)
- docs/SCRATCHPAD.md (deferred items + procedure reminders + the seven C6a lesson families)
- docs/SESSION_NOTES.md: 2026-05-12 Phase 1 wrap-up, 2026-05-15 (Phase 2 C3) audit-row formulas (line 1688), 2026-05-15 (Phase 2 C4) ListBox-with-radio-template pattern (line 1850), 2026-05-16 (Phase 2 C6a) full handoff

---

## Scope

Two dialogs (Submit-for-Review, Review-and-Sign), one ReturnToDraft dialog, one minimum-viable comment surface, and the detail-view integration that wires them all. Consumes C3's `SubmitForReviewAsync` / `SignAsReviewerAsync` / `ReturnToDraftAsync` plus one new `AddCommentAsync` method. Exercises C4's `ISignatureRolePrompter` + `SignAsRoleDialog` in production for the first time. Single commit per the C6a/C6b chunk chain.

---

## Decisions baked in (from planning conversation)

1. **Reviewer-picker UX:** full-list with substring filter textbox, active users only (`IsDeleted == false`), ~~self-assignment allowed~~ **self-assignment forbidden per C3 service-layer guard (DocumentLifecycleService.cs:112 + inline comment at :144 documenting C3 plan §G Q5); reconciliation deferred — see SCRATCHPAD "Author-as-self-reviewer policy"**. ~~Author-as-own-reviewer produces two distinct signatures (submit + review) under potentially distinct roles via the prompter — the entity model is unmodified.~~
2. **Submit affordance permission gating:** AND of `Document.SubmitForReview` + `Document.AssignReviewers`. Users with only one of the two see no submit affordance. (In strict-gatekeeper deployments where only QM holds `AssignReviewers`, only QM submits; authors create drafts that QM submits on their behalf.)
3. **ReturnToDraft surface included** with required-reason text box; signed transition; discards in-progress reviewer signatures per ADR 0008 §"Signatures reset when In Review returns to Draft."
4. **DocumentReviewComment surface included** as minimum-viable. Comment-add gated by `Document.Review`. Visible on detail view when `Lifecycle == InReview`. Comment editing/deletion/threading/markdown/notifications all deferred.
5. **Smoke-setup script:** extend existing `scripts/grant-document-permissions.ps1` rather than ship a separate script.

---

## Surfaces & files

### A. Service / repository layer extensions

Four new surfaces. All flagged here for scope-creep at stop 1; minor extensions to existing repositories rather than new top-level abstractions:

1. **`IUserRepository.GetUsersWithPermissionAsync(string permissionName, CancellationToken ct)`** — for reviewer-picker candidate load. Active users only. Mirrors C6a's `GetByIdsAsync` scope-creep precedent.
2. **`IDocumentReviewAssignmentRepository.GetByRevisionIdAsync(Guid revisionId, CancellationToken ct)`** — for detail-view assigned-reviewer panel rendering. May be a brand-new repository or an addition to an existing one; lean dedicated repository for clarity.
3. **`IDocumentReviewCommentRepository`** with `GetByRevisionIdAsync(Guid revisionId, CancellationToken ct)` — for comment panel rendering. New repository.
4. **`IDocumentLifecycleService.AddCommentAsync(Guid revisionId, string bodyText, CancellationToken ct)`** — new lifecycle service method. Single audit row: 1 comment Insert. Permission check: `Document.Review`. State check: revision must be in `InReview` lifecycle state. Reasoning for placement in lifecycle service (rather than a new comment service): keeps the review-flow methods on one surface; matches the AddCommentAsync API to the SignAsReviewerAsync API in shape and gating.

**Confirm at stop 1:** whether `ReturnToDraftAsync` currently takes a reason-string parameter. If not, that's a small service-layer extension to add before stop 5's dialog work. Audit-row count formula unchanged either way (`1 + N`).

### B. Shared UI infrastructure — reviewer picker

`EasySynQ.UI/Documents/Reviewers/`:

- `ReviewerPickerControl.xaml` + code-behind. `ListBox` with `SelectionMode="Multiple"`. ItemTemplate renders `(DisplayName + Username)` with the C4 ancestor-IsSelected pattern — selection authority stays in the ListBox; template provides visual affordance only. Checkbox variant of C4's radio-button pattern.
- `ReviewerPickerViewModel` — exposes `Candidates : IReadOnlyList<ReviewerCandidate>`, `SelectedCandidates : ObservableCollection<ReviewerCandidate>`, `FilterText : string`, and computed `FilteredCandidates` (substring match against DisplayName + Username).

### C. SubmitForReviewDialog

`EasySynQ.UI/Documents/SubmitForReview/`:

- `SubmitForReviewDialog.xaml` + code-behind. Modal, OwnedBy MainWindow. Embeds reviewer picker + optional submit-notes field + Submit/Cancel.
- `SubmitForReviewViewModel`: load reviewer candidates via the new `GetUsersWithPermissionAsync` → user filters/picks ≥1 reviewer → Submit → invoke `ISignatureRolePrompter` filtered on the author's submission-eligible roles → call `SubmitForReviewAsync` with reviewer set + resolved signing role → close on success.
- Tests: candidate-load shape, OK can-execute requires ≥1 reviewer per ADR 0008, Cancel composes to `OperationCanceledException`, multi-role-user prompt path, single-role auto-pick path, end-to-end success.

### D. ReviewAndSignDialog

`EasySynQ.UI/Documents/ReviewAndSign/`:

- `ReviewAndSignDialog.xaml` + code-behind. Confirmation surface ("Sign as reviewer of {DocumentTitle} Rev {n}? Signing as {resolvedRole}.") + Sign/Cancel.
- `ReviewAndSignViewModel`: invokes `ISignatureRolePrompter` filtered on `Document.Review` → calls `SignAsReviewerAsync` with resolved role → surfaces post-sign transition state (last-signer triggers Approved; not-last-signer leaves document InReview).
- Tests: single-role auto-pick path, multi-role picker path, cancel path, not-last-signer path, last-signer path.

### E. ReturnToDraftDialog

`EasySynQ.UI/Documents/ReturnToDraft/`:

- `ReturnToDraftDialog.xaml` + code-behind. Required-reason textarea + Return/Cancel.
- `ReturnToDraftViewModel`: reason validation (non-empty) + `ISignatureRolePrompter` filtered on `Document.ReturnForEdits` → calls `ReturnToDraftAsync` with reason + resolved role.
- Tests: reason-required for OK can-execute, multi-role picker path, cancel path, end-to-end including verification of `Pending → Discarded` assignment-row updates and preservation of `Signature` entities.

### F. Comment surface

`EasySynQ.UI/Documents/Comments/`:

- `CommentPanelControl.xaml` + code-behind. Embedded in `DocumentDetailView` when `Lifecycle == InReview`. Renders existing comments chronologically (oldest first or newest first — confirm at stop 6) with author display name + timestamp. Textarea + Add button below.
- `CommentPanelViewModel`: load existing comments via `IDocumentReviewCommentRepository.GetByRevisionIdAsync` → Add command gated on `Document.Review` + non-empty textarea → invokes `AddCommentAsync` → refreshes panel.
- Tests: comments render in correct order, Add gated correctly (permission + non-empty + InReview state), AddCommentAsync invoked with correct args, audit row written.

### G. DocumentDetailViewModel integration

C6a's detail VM gains:

- **`SubmitForReviewCommand`** — visible when `Lifecycle == Draft` AND user holds `Document.SubmitForReview` AND `Document.AssignReviewers`.
- **`ReviewAndSignCommand`** — visible when `Lifecycle == InReview` AND current user is in the assigned-reviewer list with `Status == Pending` AND user holds `Document.Review`.
- **`ReturnToDraftCommand`** — visible when `Lifecycle == InReview` AND user holds `Document.ReturnForEdits`. Scope: available to reviewers and to the author if their permissions allow it. Confirm at stop 7 whether the author's path differs from a reviewer's path materially (likely no — same dialog, same service method).
- **Assigned-reviewer panel** — read-only display when `Lifecycle == InReview`. Renders each `DocumentReviewAssignment` row with reviewer display name + status badge (Pending / Signed / Discarded). Status-badge colors per UI discipline: Pending = amber, Signed = green, Discarded = neutral/grey.
- **Comment panel** — hosted in detail view per §F; visibility per the comment-panel rules above.
- **State-driven affordance gating** — C6a's edit/replace/delete affordances must hide when `Lifecycle != Draft`. Confirm C6a's current state at stop 7 and add gating if missing.

Tests: command visibility matrix across (Lifecycle × Permission × IsNamedReviewer × IsAuthor) combinations; assignment-panel rendering by state; comment-panel rendering by state; affordance hide-when-not-Draft enforced end-to-end.

### H. Smoke-setup script extension

`scripts/grant-document-permissions.ps1` gains a `-CreateMultiRoleUser` switch (or similarly-named) that:

- Creates a second role (suggested: `ReviewerSecondary` or similar) holding only `Document.Review`.
- Creates a test user (suggested: `multireviewer` or similar) with both `QualityManager` and the secondary role assigned via `UserRole` rows.
- Applies the SCRATCHPAD #3 disciplines unchanged:
  - Owned-type column flattening awareness (`RolePermission` / `UserPermission` `EffectivePeriod` is flat `EffectiveFromUtc` / `EffectiveToUtc`).
  - DateTime text-comparison format awareness (EF Core writes space-separated, no T/Z suffix; SQLite lexical comparison excludes mismatched-format rows).
  - Idempotency check (re-running the script is a no-op if the user already exists).

---

## Stop points

Following C6a's pattern — multiple plan-implement-review cycles inside one commit:

1. **Service / repository extensions** — `GetUsersWithPermissionAsync`, `IDocumentReviewAssignmentRepository.GetByRevisionIdAsync`, `IDocumentReviewCommentRepository`, `AddCommentAsync`. Confirm `ReturnToDraftAsync` reason-string shape.
2. **Reviewer picker control** — standalone testable, foundation for stop 3.
3. **SubmitForReviewDialog + VM + tests** — first production exercise of `ISignatureRolePrompter`.
4. **ReviewAndSignDialog + VM + tests** — second prompter exercise.
5. **ReturnToDraftDialog + VM + tests**.
6. **Comment panel + AddCommentAsync wiring + tests**.
7. **DocumentDetailViewModel integration** — all commands wired; state-driven affordance gating verified end-to-end; assignment panel hosted; comment panel hosted.
8. **Smoke-setup script extension**.
9. **Smoke walk** (profile below).
10. **Commit message review → commit.**

---

## Risk-driven smoke walk

Profile: signing flow against multi-role user. Cascade-init layers settled from C6a; banner mechanism inherited for any new failure paths the dialogs introduce.

Walk steps:

1. **Submit, single-role user.** Picker loads candidates filtered by `Document.Review`; OK enables on ≥1 selection; no role prompt; document Draft → InReview; audit log shows `2 + N` rows.
2. **Submit, multi-role user.** **First production rendering of `SignAsRoleDialog`.** Picker → role-prompt → service call ordering. Audit row's `RoleAtTimeOfSign` matches pick.
3. **Review-and-sign, single-role, not-last-signer.** Command visible only for named reviewer with `Document.Review`. Signing: no role prompt. Assignment row Pending → Signed; document stays InReview; 2 audit rows.
4. **Review-and-sign, multi-role, last-signer.** Multi-role picker. Last-signer triggers automatic InReview → Approved transition atomically; 3 audit rows (no prior Active in this smoke scenario).
5. **Return-to-Draft.** Reason required. Discarded-signature path verified: assignment rows Pending → Discarded; `Signature` entities preserved; document InReview → Draft; `1 + N` audit rows.
6. **Comment-add by multi-role reviewer.** Comment Insert + 1 audit row + comment visible in panel after dialog close.
7. **Cancel paths** at each of the four dialogs. `OperationCanceledException` composes; no partial state; no orphan audit rows.

**End-of-walk cleanup checklist** per SCRATCHPAD: restore any DB direct manipulation; remove test users/comments if walk-specific; confirm working tree state.

---

## Audit-row formulas (from C3, SESSION_NOTES line 1688)

| Operation | Audit row count | Composition |
|---|---|---|
| Submit | `2 + N` | revision Update + Signature Insert + N assignment Inserts |
| ReturnToDraft | `1 + N` | revision Update + N assignment Updates |
| Sign non-final | `2` | Signature Insert + assignment Update |
| Sign final, no prior Active | `3` | Signature + assignment + revision |
| Sign final, with prior Active | `4` | Signature + assignment + new revision + prior revision |
| AddComment (new in C6b) | `1` | comment Insert |

Tests reference symbolically. No hard-coded literals.

---

## Conventions (unchanged from C6a)

- Plan-first: stop points are explicit pause-and-review moments.
- Scope-creep flag-first: anything beyond this plan surfaces at the relevant stop point before code.
- Commit authorization: commit message reviewed before `git commit`.
- No new dependencies without flagging.
- No AI attribution anywhere in tracked content.
- Test stability: 5 consecutive `dotnet test` runs at the new total before commit. Stress test if test infrastructure changes (none expected).

---

## Test growth estimate

~70–110 new tests (upward from initial ~40–70 estimate due to inclusion of ReturnToDraft and comments). Final count documented in handoff. Composition:

- Reviewer picker VM: ~8
- SubmitForReview dialog/VM: ~12
- ReviewAndSign dialog/VM: ~12
- ReturnToDraft dialog/VM: ~8
- DocumentReviewCommentRepository: ~6
- AddCommentAsync service method: ~6
- Comment panel VM: ~6
- DocumentDetailViewModel new-state coverage: ~15
- Repository extensions (GetUsersWithPermissionAsync, GetByRevisionIdAsync × 2): ~8
- Integration tests on new audit-row paths: ~5–10

---

## Definition of Done (per CLAUDE.md §10)

- [x] Spec without scope creep (ReturnToDraft + comments are explicit asks per planning conversation, not silent additions)
- [x] Unit + integration tests; coverage maintained
- [x] Audit log writes verified (C3 coverage for existing methods; new tests for AddCommentAsync)
- [x] Digital signatures used
- [x] Permission-gated per ADR 0007 / ADR 0008 catalog
- [N/A] Effective-dating (C6b is non-configuration-bearing)
- [N/A] New lockout state (C7's lock inspector covers existing ones)
- [Deferred to C7] Print-friendly rendering for detail views — C6b's dialogs are non-printable by nature; the detail-view print stylesheet is C7 territory
- [x] User manual section drafted alongside
- [x] Auditor-perspective walkthrough: every state transition produces a signed, audit-logged record traceable to user gesture
- [x] No new compiler warnings or linter violations

---

## Out of scope (defer to C7 or later)

- Lock inspector chains (C7)
- Print stylesheets (C7)
- PDF.js toolbar theming (C7 polish trio)
- Comment editing, deletion, threading, @-mentions, notifications, markdown formatting
- Reviewer markup / inline PDF annotation
- ExternalDocument surfaces (C8)
- Vault-blob orphan cleanup
- Admin UI for role/permission management
