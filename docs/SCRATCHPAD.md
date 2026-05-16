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
