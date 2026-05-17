# EasySynQ — Planning Scratchpad

Items deferred from one phase/chunk to a later one. Not authoritative
(that's SPEC.md + ADRs + SESSION_NOTES.md); just a place to keep
deferred-but-not-forgotten work so the next planner picks it up
without re-discovering it.

Each entry should record: which chunk surfaced it, where the original
discussion lives, and what the work is. Remove the entry when its
target chunk lands and the work is complete.

---

## C7 (lock inspector + print views) — visual polish surfaces

Three items collected here. They share a "make visual surfaces match
the rest of the app" character and warrant being thought about
together when C7 planning happens.

### PDF.js viewer toolbar theming

- **Surfaced by:** Phase 2 C6a smoke walks #1 / #2 (2026-05-16).
- **Background:** ADR 0010 C5's `Assets/pdfviewer/web/easysynq-overrides.css`
  scoped strictly to hiding download/open-file affordances. Toolbar
  theming wasn't called out, so PDF.js's stock light-toolbar styling
  bleeds through against the dark EasySynQ host theme — page number,
  zoom controls, search input are unreadable.
- **What to add:** CSS rules in `easysynq-overrides.css` (or a sibling
  file under `Assets/pdfviewer/web/`) targeting PDF.js 5.7.284's
  toolbar IDs: `#toolbarContainer`, `#toolbarViewer`,
  `#toolbarViewerMiddle`, `.toolbarButton`, `.toolbarField`,
  `#findbar`, `#secondaryToolbar`. Map background to BrushSurface(2),
  text/icons to BrushText/BrushTextDim, input fields to
  BrushSurface2 + BrushText.
- **Verification cost:** Per the C5 version-bump lesson — every new
  rule earns a "re-verify selector against bundled PDF.js" step on
  any future PDF.js version bump. Worth bundling these all into a
  single dedicated commit so the bookkeeping is concentrated.
- **Scope estimate:** 30–60 minutes of CSS-against-real-app
  iteration.
- **NOT to be confused with:** the cross-origin / virtual-host fix
  that landed in C6a (smoke walk #3). That one was a functional bug
  in the viewer's PDF-load path — separate concern from toolbar
  theming. See ADR 0010's 2026-05-16 amendment.

### Lock inspector

Per ADR 0008 C7 scope — every lockout state surfaces in the lock
inspector per SPEC §4.3. Touches every Phase 2 lockout pathway.

### Print-friendly Document + Revision detail rendering

Per SPEC §4.5. Print stylesheet for the Document detail view and
the Revision history (when that surface lands).

---

## Detail view loses affordances on document switch (deferred from C6b stop 9)

- **Surfaced by:** Phase 2 C6b stop 9 (2026-05-16). After clicking
  one document then clicking a second, the second document's
  affordance row appears empty/stale. Workaround: navigate to
  another module then back into Documents, which forces a full
  rebuild of the list+detail panes.
- **Suspected family:** C6a lesson #2 (event-handler subscription
  discipline) — stop 7 added new state (`Assignments`,
  `CommentPanel`, `_assignmentReviewers`) plus three new commands
  to the `DocumentDetailViewModel`. The list VM's
  `DetailViewModel` getter calls `ClearDetailViewModel` then
  constructs a fresh VM via `_detailFactory`. WPF's
  `ContentControl + DataTemplate` should tear down the existing
  view and instantiate a new one (whose `Loaded` event fires
  `LoadCommand`).
- **What's known to be correct:**
  - 50/50 unit tests for `DocumentDetailViewModel` pass including
    the C6b additions; VM-side logic is verified.
  - `CurrentRevision`'s `[NotifyPropertyChangedFor]` includes all
    seven C6b properties + the four `Can*` derived flags so
    PropertyChanged fires correctly when `LoadAsync` updates it.
  - `LoadAsync` clears `Assignments` to `Array.Empty<>` and
    `CommentPanel` to `null` when the new revision is not in
    InReview (no stale-state leak through the property path).
- **Zones to investigate first when picking this up:**
  1. WebView2's WPF lifecycle. PdfViewerControl embeds WebView2;
     historical issues exist around ContentControl rebuild +
     WebView2 dispose timing. May be blocking the new view's
     `Loaded` event from firing on the second selection.
  2. `OnLoadedAsync` in `DocumentDetailView.xaml.cs` checks
     `DataContext is DocumentDetailViewModel vm` — if DataContext
     hasn't been set yet when Loaded fires, `LoadCommand` isn't
     dispatched. Add a `DataContextChanged` handler as a fallback
     trigger.
  3. The `ActivatorUtilities.CreateInstance` factory resolves
     scoped services from the singleton root provider (existing
     C6a quirk). If any of the new C6b dependencies hold per-
     instance state that conflicts across detail VMs, that's a
     candidate.
- **Why it's deferred:** diagnosis requires running the WPF app
  and stepping through the rebinding lifecycle; the unit-test
  layer can't easily reproduce. The workaround is mild (one
  extra click). Punt to a follow-up commit named something
  like "fix(ui): detail-view affordance refresh on selection
  switch (C6b follow-up)".

---

## ADR 0009 role-resolution vs direct UserPermission grants (deferred from C6b stop 9)

- **Surfaced by:** Phase 2 C6b stop 9 (2026-05-16). Smoke walk
  Step 1 surfaced the gap cleanly via `SignatureRolePrompter`'s
  defensive `InvalidOperationException("the current user holds
  no role that grants this permission")`. The throw catches the
  case correctly — it just blocked the smoke walk.
- **Architectural shape:** ADR 0009's `IRoleResolutionService.
  GetEligibleRolesForPermission(permissionName)` filters the
  current user's `ICurrentUserAccessor.RolePermissions` snapshot
  (a per-role permission map). Permissions granted only via
  direct `UserPermission` rows (ADR 0007 §"per-user permission
  grants") never appear in any role bucket, so the prompter
  finds zero eligible roles and throws. The user effectively
  holds the permission (auth-check passes) but the prompter
  doesn't know which role-string to stamp on the signature.
- **Short-term mitigation (C6b stop 9 smoke):** the
  `grant-document-permissions.ps1` script grants the smoke
  author the seeded QualityManager role via a `UserRole` row.
  QualityManager grants most Document.* perms. The direct
  UserPermission grants for the same perms remain harmless
  redundancy (effective-permission set is a union); the
  prompter now sees QualityManager as an eligible role and
  auto-picks for single-role users.
- **Long-term fix:** an ADR 0009 amendment documenting how
  signing should be handled when the user's only path to a
  permission is a direct UserPermission grant. Candidate
  approaches:
  1. **Synthesize a "DirectGrant" role string** when no real
     role applies. Simple but creates an audit-log oddity (the
     `Signature.RoleAtTimeOfSign` value points to a string
     that's not a real role).
  2. **Require every signable permission to be in at least one
     role** in the deployment, validated at bootstrap or admin-
     UI grant time. Conservative — closes the gap by
     construction rather than handling it.
  3. **Drop the role-snapshot filter** and use the flat
     `Permissions` collection instead, picking the user's
     "default" role (e.g., their first role alphabetically) as
     the `RoleAtTimeOfSign`. Loose; loses the per-role-
     attribution semantics ADR 0009 was designed around.
  Chooser of the long-term fix should also revisit ADR 0007
  §"Alternatives Considered" — the direct-grant deployment
  shape was already flagged as a corner case there.

---

## Smoke-script grants paper over a seed-shape gap (lesson pinned C6b stop 9)

- **Surfaced by:** Phase 2 C6a + C6b smoke walks (2026-05-15
  through 2026-05-16). Each phase's smoke walk has added
  `UserPermission` direct grants and (at C6b) a `UserRole`
  membership to the smoke users so the C6a/C6b affordances
  render. Treated initially as "scripts grow per-phase"; the
  deployment-model clarification at stop 9 reframes it: these
  grants are smoke pragmatism papering over a real
  seed-shape gap, not legitimate ongoing maintenance.
- **Deployment-model intent (user clarification, 2026-05-16):**
  Administrator is the **IT-side seat**, not a QMS operator;
  it administers users/roles/permissions but does not author,
  submit, review, or sign documents. The **small-shop default**
  is "users who can author drafts can inherently submit
  them" — author-can-submit, where `Document.SubmitForReview`
  and `Document.AssignReviewers` both reach authors via their
  operational role. Strict-gatekeeper deployments (only QM
  submits / only QM assigns) are the configurable exception,
  not the default.
- **What the seed actually does:** `PermissionNames.All`
  (consumed by `BootstrapService.CreateAdministratorAsync`)
  is system-only — Administrator gets no Document permissions
  at bootstrap. The migration-seeded `QualityManager` role
  holds most Document permissions but **explicitly omits**
  `Document.AssignReviewers` (per ADR 0008 §"Authorization");
  neither QM nor any other seeded role grants it. So on a
  fresh-bootstrap install:
  - No user holds `Document.AssignReviewers` at all.
  - The author-can-submit small-shop default is **not** the
    default — it requires an explicit admin-UI grant that
    doesn't exist yet.
  - Smoke walks that exercise submit-for-review can't run
    without scripted direct grants.
- **Four instances of the script-grants-paper-over-seed-gap
  pattern catalogued so far:**
  1. **C6a (Document.HardDelete to admin):** script grants
     admin `Document.HardDelete` directly so the Draft
     hard-delete affordance renders. Smoke pragmatism — admin
     isn't operationally hard-deleting Drafts in production.
  2. **C6b (four-permission grant to admin):** script grants
     admin `Document.SubmitForReview`, `Document.AssignReviewers`,
     `Document.ReturnForEdits`, `Document.Review` directly. Same
     smoke pragmatism — admin isn't a reviewer or a submitter.
  3. **C6b (admin → QualityManager UserRole membership):**
     script writes a `UserRole` linking admin to QualityManager
     so the role-prompter (ADR 0009) finds an eligible role
     for admin's submit-for-review signature. Without it, the
     prompter throws the defensive "no role grants this
     permission" exception even though admin has the direct
     grants. Again, smoke pragmatism — admin isn't a member of
     QualityManager in production.
  4. **C6b (multireviewer Document.AssignReviewers direct
     grant):** script grants multireviewer `AssignReviewers`
     directly. Multireviewer IS an operational user (in
     QualityManager), so this is the closest of the four to
     a legitimate production grant — but it works around the
     fact that the seed doesn't include AssignReviewers in
     any operational role. In a production small-shop install
     this grant would have to come from the admin UI per
     ADR 0008's "organizations grant separately" framing.
- **List-view vs detail-view permission-check parity
  property** (testable invariant for the follow-up fix): a
  user signed in via the production sign-in flow should have
  the same effective-permission view in both the list and
  detail VMs. Step 2 surfaced an asymmetry — multireviewer's
  `Document.Create` check worked in the list (Doc B was
  created successfully) but the same user's
  `Document.EditDraft` / `Document.HardDelete` checks
  appeared to fail in the detail view. If the architectural
  follow-up correctly seeds the operational roles, this
  asymmetry would still surface any latent VM-side bug
  (independent of the seed shape).
- **Architectural follow-up (NOT in C6b — focused commit or
  ADR amendment):** reconcile the seed-shape gap so the
  deployment-model intent is the out-of-the-box default.
  Three candidate paths to surface in the follow-up's
  planning conversation:
  - **(a) Amend QualityManager seed to include
    `Document.AssignReviewers`** — small-shop default:
    anyone in QualityManager (which is most operational
    users) can submit. Strict-gatekeeper deployments revoke
    AssignReviewers from QualityManager and grant it to a
    dedicated role.
  - **(b) Reconsider ADR 0008's rejection of a seeded
    Author role** — given the deployment-model clarification
    that author-can-submit is the intended default, a
    seeded Author role with Create + EditDraft +
    SubmitForReview + AssignReviewers may be the right
    shape. QualityManager keeps the review-side permissions.
  - **(c) Hybrid** — keep strict-gatekeeper deployment as
    a configurable option (e.g., a bootstrap-time
    `--strict-gatekeeper` flag) while making the small-shop
    default work out-of-the-box.
  Each option has trade-offs worth working through in a
  focused planning conversation, not under smoke-walk time
  pressure.
- **Follow-up commit's cleanup task:** when the seed is
  reconciled, audit `scripts/grant-document-permissions.ps1`
  and remove the four pattern instances above. The script
  should retain only the
  multireviewer/secondreviewer/ReviewerSecondary mint logic
  (which is genuine test-data, not a seed-gap workaround) +
  a thin verification helper that prints the smoke author's
  effective-permission set for pre-flight assertions.

---

## Assignment panel role display (deferred from C6b stop 7)

- **Surfaced by:** Phase 2 C6b stop 9 (2026-05-16). The stop-7
  assigned-reviewer panel renders
  `(ReviewerDisplayName, ReviewerUsername, Status)` but not the
  `RoleAtTimeOfSign` snapshot captured on the reviewer's
  `Signature` row.
- **Why it matters:** for a Signed assignment, the role the
  reviewer signed under (`Signature.RoleAtTimeOfSign`) is the
  authoritative audit-trail field. Surfacing it next to the
  status badge gives a clearer "who signed under which capacity"
  view, especially for multi-role users.
- **What to add when picked up:**
  1. Extend `AssignedReviewerRow` with a nullable
     `RoleAtTimeOfSign` field (null for Pending/Discarded).
  2. In `DocumentDetailViewModel.LoadAsync`, when projecting
     assignments, look up each Signed assignment's
     `SignatureId → Signature.RoleAtTimeOfSign` (one batch query
     via a `GetByIdsAsync` on `ISignatureRepository` or similar).
  3. Bind the field in `DocumentDetailView.xaml`'s assignment
     `ItemTemplate` — e.g., "signed as {RoleAtTimeOfSign}" caption
     line under the status badge.
- **Smoke walk impact:** not a blocker. The walk verifies the
  Signature row exists with the correct `RoleAtTimeOfSign` via
  raw SQL inspection; the UI's omission is cosmetic for now.

---

## Author-as-self-reviewer policy (deferred from C6b stop 9)

- **Surfaced by:** Phase 2 C6b stop 9 (2026-05-16). Plan §1 and §C
  both claim "self-assignment allowed" on the submit-for-review
  reviewer picker; the C3 service implementation
  (`DocumentLifecycleService.SubmitForReviewAsync`) rejects it with
  `ArgumentException("Author cannot review their own document.")`.
- **Provenance:** C3 commit `0ae4317` message and inline comment at
  `DocumentLifecycleService.cs:144-146` ("Author may sign as a
  reviewer in real workflows, but we forbid the author appearing
  in their OWN reviewer list per Q5") confirm this was a
  deliberate planning-time decision, not defensive coding. ADR
  0008 is silent — Q5 was a conversation-level architectural
  choice that never got elevated.
- **Decision (C6b stop 9):** the C3 guard wins for now; C6b plan
  §1/§C wording is treated as a misnomer. The smoke walk runs
  with a `secondreviewer` test user instead of admin
  self-assigning. The C6b commit message will flag the plan-vs-
  service reconciliation explicitly.
- **What to do when picked up:**
  1. If the override is the right call, write an ADR titled
     something like "0011-author-as-self-reviewer-policy" arguing
     the case (e.g., "the submit signature attests to readiness;
     a separate review signature attests to validation under a
     potentially distinct role"). Then remove the C3 guard, the
     unit test that exercises it, and amend ADR 0008 to reference
     the new ADR.
  2. If the C3 guard is correct, amend the C6b plan §1 and §C
     wording on a future planning-doc pass (the plan is a
     working document, not an authoritative artifact, so the
     amendment is bookkeeping).
- **Other filter sites are absent** — the reviewer-picker query,
  the picker VM, and the SubmitForReview VM all let the author
  appear in candidates. Service-layer rejection is the only
  enforcement. If option 1 above is chosen, those layers don't
  need changes.

---

## Submit-for-review notes field (deferred from C6b stop 3)

- **Surfaced by:** Phase 2 C6b stop 3 (2026-05-16). Plan §C lists an
  "optional submit-notes field" in the dialog markup, but the
  underlying `IDocumentLifecycleService.SubmitForReviewAsync` has no
  notes parameter and the plan's audit-row table keeps Submit at
  `2 + N` (no comment Insert folded in).
- **Decision:** omit the notes field from stop 3. Reviewing author
  + user agreed shipping the textbox as dead UI would mislead users
  (typed notes silently discarded).
- **What to add when picked up:**
  1. Either extend `SubmitForReviewAsync` with a `string? submitNotes`
     parameter that, when non-null/non-whitespace, writes a
     `DocumentReviewComment` in the same transaction (audit count
     becomes `2 + N + 1 = 3 + N` when notes present); or
  2. Add a dedicated field on `DocumentRevision` analogous to
     `LastReturnToDraftReason` (stop 1), captured on submit.
- **Plan implication:** the C6b plan's "Audit-row formulas" table
  needs an amendment if option 1 is chosen.

---

## Reminders for next phase's smoke-procedure writing

These are procedural lessons (not numbered handoff lessons) — process
discipline for writing the smoke walk in C6b / C7 / future phases.

- **Optional failure-injection steps come BEFORE destructive
  operations.** Smoke walk #5 had the banner-injection test scheduled
  AFTER hard-delete; the hard-delete took away the document the
  injection needed. Either order optional-failure-tests before
  destructive ones, or make the optional step explicit about needing
  a fresh document if executed after hard-delete. C6a smoke ordering
  worked despite the bug because the banner mechanism was already
  exercised live during walk #3-4 diagnostics.

- **Restore any deliberately corrupted state at the end of each
  diagnostic experimentation pass.** Walks #3-4's banner-injection
  step renamed a vault file to `.disabled`; the restore step was
  not run; walk #5's create-dialog dedup-hit on the corresponding
  hash → document pointed at the missing file → unhandled
  `FileNotFoundException` → unrelated `[ERR]` in the log file that
  cost time to diagnose during the post-walk verification sweep.
  Pin a "cleanup checklist" at the end of any smoke procedure that
  touched disk state outside the app: rename-back, undelete,
  unsuspend, etc.

- **Commit message tables risk shell-quoting issues; prefer bullet
  lists for multi-row data in commit bodies.** The C6a commit's
  first HEREDOC attempt failed because a Markdown table in the
  Phase 2 chain-status section contained `|` characters that
  bash's parser tripped on even inside a single-quoted HEREDOC.
  The workaround (write to a temp file, `git commit -F`) recovered
  the commit but lost the table format. For commits with multi-row
  data, default to bullet-list format up front — same information,
  no shell-quoting surface. The table-via-HEREDOC pattern can come
  back when we have a cleaner mechanism (e.g., commit-message
  templates stored in repo, helper script that pipes via stdin).

## Reminders for the C6a closing handoff (when C9 lands)

Lessons accumulated during the smoke pause that should be pinned at
the eventual C6a handoff under appropriate sections (collecting here
so they don't get lost during the C7/C8/C9 work between now and the
handoff write-up):

1. **Audit-log-as-ground-truth for diagnosing UI errors.** When the
   UI surfaces an exception, the audit log is the fastest path to
   determining whether the operation actually executed. C6a smoke
   Finding 3 looked like a service-layer bug (KeyNotFoundException
   from HardDeleteDraftAsync); the audit log showed the operation
   had already succeeded, turning the diagnosis into a presentation-
   layer bug (stale row visible, user double-clicked). Same family
   as "verify against actual migration, not framework convention" —
   both are about checking concrete evidence rather than inferring
   from how code should behave.

2. **Event-handler subscriptions in computed properties need explicit
   unsubscription discipline.** The
   `DocumentListViewModel.DetailViewModel` pattern (lazy-construct
   VM in getter, cache, recompute on selection change) requires
   paired subscribe/unsubscribe to avoid silent stale-handler leaks
   across navigation. C6a's `ClearDetailViewModel` helper is the
   working precedent; class-level convention going forward for any
   cached-VM-with-events surface.

3. **Raw-SQL scripts against EF Core-managed columns are fragile in
   non-obvious ways.** Two C6a smoke pauses surfaced the same family
   of bug: owned-type column flattening (looks like prefixed columns,
   is actually flat) caught at script-write time, and DateTime
   text-comparison format (script wrote ISO-8601 'T'/'Z' shape; EF
   uses space-separated no-suffix and SQLite's lexical comparison
   excludes mismatched-shape rows) caught at smoke. Verify both
   schema shape (column names, owned-type flattening) and value
   format (text-stored types whose round-trip depends on EF Core's
   specific serialization) against actual migration output and
   actual EF-written rows. Convention isn't enough.

4. **"Loaded but mis-styled" vs "didn't load at all" — disambiguate
   before reaching for the polish deferral.** C6a smoke walk #1 saw a
   PDF viewer with toolbar visible + page area solid black. I read
   this as a theming issue ("PDF.js's stock light toolbar against
   dark host theme") and deferred to C7 polish. Walk #2 revealed the
   page area being black wasn't "loading state on a dark theme" — it
   was "PDF.js gave up; no document loaded." Two states overlaid:
   viewer alive (toolbar renders) + document missing (page canvas
   stays unfilled). The disambiguator is **what's IN the page area**
   — a spinner / "Loading..." text means PDF.js is still trying; a
   solid uniform color means PDF.js has given up. The polish
   hypothesis (toolbar styling) is appealing because it's a known
   small-scope fix; the load-failure hypothesis is the actual one.
   Don't reach for the polish-deferral until you've ruled out the
   functional failure.

5. **Silent-failure event subscribers are bugs.** Every event a
   control raises needs a subscriber, or the control should not
   raise it. C5's `PdfViewerControl.NavigationFailed` declared and
   raised the event but no host subscribed, so PDF-load failures
   were invisible — masking the cross-origin bug for two full smoke
   walks. C6a's fix landed a viewer-error banner in the detail view
   that surfaces failure reasons. Generalization: when adding a
   control that publishes a failure event, also wire a host-side
   consumer (banner / toast / log line) in the same commit. The
   failure path is part of the contract, not a polish layer.

6. **WebView2-hosted features have FOUR observability layers for
   cascading-init failures; complete coverage depends on which
   layers your failure modes touch.** A view that hosts a WebView2
   showing any non-trivial in-page JS framework (PDF.js, a SPA, an
   embedded editor, etc.) can fail at four distinct layers, each
   with its own host-side surface:
   - **Layer 0 — VM-side load.** Errors raised in the host view-
     model's `LoadAsync` (or equivalent) BEFORE any URL is handed
     to the viewer — typically vault / repository / service-tier
     I/O failures that prevent constructing the URL at all. Not
     observable through any WebView2 event because the WebView2
     never gets invoked. Caught by wrapping the VM's load body in
     try/catch and routing the exception through the same banner
     mechanism Layers 1+2 use.
   - **Layer 1 — Outer navigation.** `CoreWebView2.NavigationCompleted`
     fires with `IsSuccess` indicating whether the top-level
     `Navigate(...)` succeeded. Covers viewer.html failing to
     load, scheme failures, fundamental WebView2 init issues.
   - **Layer 2 — Sub-resource fetches.** `CoreWebView2.WebResourceResponseReceived`
     (or `WebResourceRequested`) fires for every fetch the
     renderer makes — sub-resource imports, XHR/fetch calls,
     iframes. Covers 404s, CORS blocks, server errors on
     anything the in-page code fetches.
   - **Layer 3 — JS-internal errors.** Errors raised inside the
     loaded page's own code (validation rejections, parse
     failures, framework exceptions) before/instead of any
     fetch. No host-side WebView2 event fires; surfaced only via
     a JS-to-host bridge using
     `AddScriptToExecuteOnDocumentCreatedAsync` +
     `WebMessageReceived` subscribing to the framework's own
     error events.

   C6a covers Layers 0+1+2 via the viewer-error banner (Layer 0
   via try/catch in `DocumentDetailViewModel.LoadAsync`; Layers 1+2
   via the existing event subscriptions). Layer 3 is deliberately
   not covered because the URL-layer fix (HOSTED_VIEWER_ORIGINS
   patch) means PDF.js's only expected Layer-3 rejection no longer
   fires. The smoke-walk sequence in C6a each surfaced a different
   layer: walks #1-2 = Layer 1 (cross-origin file://), walk #3 =
   Layer 2 (viewer-host scope), walk #4 = Layer 3 (validateFileURL),
   walk #5's post-verification = Layer 0 (vault file missing,
   `FileNotFoundException` in `GetVaultFilePathAsync`).

   When adding a WebView2-hosted feature, **map your expected
   failure modes against all four layers** before deciding which
   to instrument. Skipping a layer is a deliberate decision
   bounded by what failure modes you've ruled out at the layer
   below, not an oversight.

   The C6a banner is fed from **three input wires** — one per
   covered layer:
   1. `PdfViewerControl.NavigationFailed` raised from
      `NavigationCompleted.IsSuccess == false` (Layer 1).
   2. `PdfViewerControl.NavigationFailed` raised from
      `WebResourceResponseReceived` non-2xx on the content host
      (Layer 2).
   3. `DocumentDetailViewModel.LoadAsync`'s try/catch around vault
      I/O → direct call to `OnViewerNavigationFailed` (Layer 0).

7. **Visible-effect hypotheses are tempting but unreliable —
   check the actual error trail before reaching for them.** Three
   times during C6a smoke debugging the first diagnostic framing
   was wrong because it reached for the most visually-suggestive
   hypothesis instead of the actual evidence chain:
   - **Finding 3** (walk #1): KeyNotFoundException from
     HardDeleteDraftAsync framed as "VM passing wrong Id type to
     service." Actual cause: VM passes the right Id; first
     hard-delete succeeded; list view didn't refresh; user
     double-clicked. Audit log was the disambiguator.
   - **Finding 1** (walk #1, re-diagnosed in walk #2): Black PDF
     area framed as "stock light toolbar against dark host —
     toolbar theming polish." Actual cause: PDF.js's PDF fetch
     was being blocked by Chromium's same-origin policy; the
     toolbar IS visible because viewer.html loaded, but the page
     canvas is unfilled because no bytes ever arrived. DevTools
     Network tab was the disambiguator.
   - **Issue A** (walk #3): Black PDF area + only viewer-host
     resources loading framed first as "cross-origin CORS
     blocking the content fetch." Actual cause: PDF.js never
     reached the content fetch — viewer.mjs failed to import its
     own library (../build/pdf.mjs) because the viewer-host
     mapping was scoped at .../web/ instead of the parent
     directory containing both web/ and build/. DevTools Console
     was the disambiguator.

   The recurring pattern: a concrete visual symptom (black area,
   error message, unexpected exception) gets matched to the
   most-recently-touched-or-most-similar-sounding code path, and
   the diagnosis stops before checking the actual evidence trail
   (audit log, network requests, JS console, etc.). The cost is
   misdiagnosis → wrong fix → re-smoke cycle. The discipline is
   the same as the audit-log-as-ground-truth lesson (4) and the
   raw-SQL-against-EF-columns lesson (3) — same family,
   different surfaces: **check ground truth before inferring
   from how code should behave.** When the symptom is visual, the
   ground truth is in the failure trail (Network / Console / log
   file / audit DB), not in the screen pixels.

---

# Future-phase planning guidance

Watch-items that don't belong to a specific deferred chunk but
should land in front of a phase-N planner's eyes before they
finalize migration shape, seed-data layout, or other
chain-wide structural decisions. Add entries here when an ADR's
Consequences or a session-handoff lesson identifies a
generalizable concern; remove when the concern stops applying
or graduates to its own ADR.

---

## Seed-affecting migrations are fragmenting per chunk — consider bundling per phase

- **Surfaced by:** ADR 0011 §Consequences > Negative (2026-05-16).
  Cited verbatim in the ADR text; pinned here so a phase-N
  planner finds it without having to grep Phase 2's old ADRs.
- **Background:** Phase 2's migration chain now contains three
  seed-affecting migrations after C1: C1 itself (catalog + the
  initial `QualityManager` row), `AddVaultPhysicalDeletePermission`
  (added the `Vault.PhysicalDelete` permission + an upgrade-path
  link row), and `AddDocumentAuthorRoleAndAmendQualityManagerSeed`
  (added the `DocumentAuthor` role + amended `QualityManager`).
  Each landed in its own chunk as the work surfaced the need —
  reasonable in isolation but the cumulative shape is a
  fragmented list of small data-only migrations every fresh
  install has to apply in order.
- **What to do when picked up:** at phase-N planning time, look
  at the phase's overall seed-amendment surface before
  committing to one migration per chunk. If two chunks both
  want to amend the same seed (a permission catalog, a default
  role's permission set, etc.), consider whether their seed
  amendments can land together at the start (or end) of the
  phase rather than once per chunk. The benchmark is:
  - One catalog-amendment migration per phase is the comfortable
    default — every install applies it once, the rationale lives
    in one place.
  - Two is acceptable if a clear ordering dependency motivates
    the split (e.g., one needs to run before another's chunk so
    its referenced rows exist).
  - Three or more should be a planning-time question, not a
    discovery-time accident.
- **Why this isn't a rule:** sometimes a chunk genuinely
  discovers the need for a seed amendment mid-implementation
  and the smallest possible follow-up migration is correct
  (Phase 2's `AddVaultPhysicalDeletePermission` was that
  pattern; ADR 0011's reconciliation was another). The
  guidance is to surface the question at planning time so the
  split is deliberate, not to forbid mid-phase additions.
- **Citation:** ADR 0011 §Consequences > Negative, fourth
  bullet ("The migration is the third seed-affecting Phase 2
  migration after C1.").
