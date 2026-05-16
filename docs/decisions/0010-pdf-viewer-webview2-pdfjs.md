# ADR 0010 — PDF Viewer Dependency (WebView2 + PDF.js)

**Status:** Accepted (amended)
**Date:** 2026-05-15 (Proposed), 2026-05-15 (Accepted), 2026-05-16 (Amended — virtual host mapping required; see §"Subsequent finding" at end)
**Supersedes:** None
**Related:** ADR 0001 (lean stack discipline — this ADR introduces the project's first third-party UI dependency); ADR 0008 (Phase 2 scope — C5 paired with this ADR per the chunking); SPEC §5.1 (Document Controller's embedded-PDF-viewer requirement)

---

## Context

SPEC §5.1 requires "Embedded PDF rendering. Documents are read-only inside the app." Phase 2's Document Controller uses this to display vault-stored PDFs in the document detail UI. Phase 2 C5 implements the viewer integration, but the choice of PDF rendering library is a real decision: the project does not yet depend on any third-party PDF library, and CLAUDE.md rule 9 ("No new dependencies without flagging it first") makes the choice an explicit ADR matter.

The project also has hard architectural constraints that bear on the choice. SPEC §3.4 establishes "No cloud services. No third-party identity providers. No external PDF viewers." The phrase "no external PDF viewers" in context (paired with "no cloud services, no third-party identity") rules out cloud-hosted PDF rendering and external-process viewers (launching Adobe Reader as a separate program). An in-process rendering library that runs locally is consistent with the spec.

The deployment context is a desktop application running on Windows in manufacturing facilities. Users review SOPs, work instructions, and customer specifications via PDF. Documents range from a few pages to 100+ pages. Users need text search within documents (a compliance officer locating a specific clause), print integration (SPEC §4.5 requires print-friendly rendering), and visual fidelity that doesn't introduce confusion compared to other PDF viewers users may be familiar with.

The pilot deployment runs on Windows 10 and Windows 11 machines under facility IT control. Installation is performed by the user's IT team, not by end users from an installer link. This shapes which dependency footprints are acceptable.

Four candidate libraries were evaluated: PdfiumViewer (WPF wrapper over Google's PDFium engine), WebView2 + PDF.js (Microsoft's embedded-browser control hosting Mozilla's JavaScript PDF renderer), PdfPig with custom WPF rendering (managed PDF parser; no included renderer), and newer-generation PDFium wrappers (e.g., Caly). All four pass the hard requirements (offline-capable, license-compatible with desktop QMS deployment, read-only API, search-capable, print-capable, WPF-hostable). The differentiation is in maintenance health, rendering fidelity, deployment cost, and integration complexity.

## Decision

### WebView2 + PDF.js, hosted in a WPF UserControl

The Document detail view embeds a `Microsoft.Web.WebView2.Wpf.WebView2` control configured to load a local copy of PDF.js bundled with the application. PDFs from the vault are loaded into the WebView2 instance by setting the source to a file URI pointing at the bundled PDF.js viewer with the vault file as a query parameter.

Concretely:

- `pdf.js` and its viewer assets ship in the application's installation directory under `pdfviewer/` (or similar — exact path resolved at C5 implementation time).
- The Document detail ViewModel exposes the vault file path of the current revision (resolved via `IVaultService.RetrieveAsync`'s sibling path-lookup method — see implementation notes).
- The viewer control loads `file:///pdfviewer/web/viewer.html?file=<file-uri-of-vault-pdf>`.
- PDF.js renders, provides built-in toolbar (zoom, page navigation, search, print, download).
- The application configures PDF.js's UI to disable download (per the "documents are read-only inside the app" SPEC requirement; users acquire copies through controlled export, not browser-style downloads).

### Reasoning summary

Both WebView2 + PDF.js and PdfiumViewer were strong candidates. The choice in favor of WebView2 + PDF.js rests on three points, in order of weight:

1. **Maintenance health.** WebView2 is actively maintained by Microsoft for current .NET versions; PDF.js is actively maintained by Mozilla as the rendering engine for Firefox's built-in PDF viewer. Both organizations have multi-year, predictable update cadences. PdfiumViewer's original repository has been quiet for an extended period; while forks exist, none has the same maintenance assurance.

2. **Rendering fidelity.** PDF.js handles a wider range of PDF edge cases (rotated text, embedded fonts, form-field display in read-only mode, signature affordances) with closer-to-Adobe-Reader fidelity than PdfiumViewer's bitmap-rendered pages. For compliance-critical documents, "the PDF looks slightly wrong" is a real problem; PDF.js's track record minimizes that risk.

3. **Feature completeness.** PDF.js ships with a complete viewer UI: zoom, page navigation, search, print, fullscreen. PdfiumViewer is a rendering library — the viewer UI (search box, page numbers, zoom controls) is the integrator's responsibility. WebView2 + PDF.js saves the equivalent of several days of UI-glue work.

### Accepted cost: WebView2 runtime deployment dependency

WebView2 requires the WebView2 Runtime to be present on the target machine. Three deployment options exist:

- **Evergreen runtime (default Microsoft recommendation):** Installed system-wide, auto-updated by Microsoft. Ships preinstalled with Windows 11 and modern Windows 10 builds. Older Windows 10 machines without it can install it through Microsoft Update or the standalone installer (~150 MB download, one-time).
- **Fixed-version runtime (bundled with the app):** Specific WebView2 version shipped alongside the application. Adds ~150 MB to installer size. Application is insulated from runtime-version changes but loses automatic security updates.
- **Bootstrapper:** Installer-time check; downloads Evergreen if not present, with user consent. Smaller installer but requires network at install time.

**EasySynQ ships with the Evergreen runtime as a prerequisite, documented in deployment instructions.** Pilot deployments are IT-managed and the runtime is reasonable to assume present on supported Windows versions. The "bundle a fixed-version runtime" option is rejected for the installer-size cost; the bootstrapper option is rejected because it requires network at install time, which may not be available on air-gapped facility installs.

Deployment documentation (the user manual section that ships with C5) explicitly states the WebView2 Runtime as a system prerequisite, with installation instructions for facilities running older Windows 10 builds.

### What ships in C5

The C5 commit lands:

- `Microsoft.Web.WebView2` NuGet package dependency added to `EasySynQ.UI.csproj`. CLAUDE.md rule 9 satisfied: dependency flagged in this ADR, justified by the requirements, and added in the commit that consumes it.
- PDF.js distribution (the "release" build with the bundled viewer) added to the repo at `assets/pdfviewer/` (or similar — path resolved at C5 implementation time). Bundled into the build output via the csproj's copy-to-output directive.
- A `PdfViewerControl` (or similar — name resolved at C5) in `EasySynQ.UI/Documents/Controls/` wrapping the WebView2 instance. Exposes a `LoadDocument(string vaultFilePath)` method or a `DocumentPath` dependency property.
- `IVaultService` gains a sibling lookup method `GetVaultFilePathAsync(Guid blobId, CancellationToken ct)` returning the on-disk path for a blob without opening a stream. The existing `RetrieveAsync` opens the file; the viewer needs the path to hand to WebView2's URI loader, not a stream. This is a real but small contract addition flagged at plan time per the C2 scope-creep convention.
- Document detail view's ViewModel and View wired to display the viewer when the current revision has a VaultBlob attached. Revisions without a blob (a Draft revision whose author hasn't uploaded a file yet) show a placeholder.
- PDF.js viewer configuration to disable download (the UI surface that lets users save the PDF to their machine — inconsistent with the "controlled export" model). Other PDF.js viewer features (zoom, search, print, page navigation, fullscreen) remain enabled.

What does **not** ship in C5:

- Controlled export (the deliberate user-facing "export this document with these signatures" flow). Deferred to C8 or later.
- Annotation, markup, redlining (out of scope per SPEC §5.1 "documents are read-only inside the app").
- Authoring or upload flows (those land in C6 with the rest of the document UI shell).
- Auto-recovery from missing WebView2 Runtime (if the runtime is missing at app launch, the app shows a clear error message pointing to deployment docs; auto-install is out of scope).

### Integration approach

The WebView2 control is wrapped in a thin WPF UserControl rather than embedded directly in the Document detail view. Reasoning: keeps the WebView2 lifecycle (initialization, navigation, disposal) encapsulated behind a stable API, and means future replacement (if WebView2 ever becomes untenable) is a one-control swap rather than a view-level refactor.

The UserControl exposes:

- `DocumentPath` dependency property (or equivalent) — full path to the PDF file on disk. Setting it triggers viewer navigation.
- `IsViewerReady` observable property — true once WebView2 has initialized. Useful for the parent VM to coordinate loading indicators.
- `NavigationFailed` event — surfaces if the PDF fails to load (file missing, file corrupt, viewer assets missing).

WebView2 initialization is async; the UserControl handles the async lifecycle internally and presents a synchronous interface to consumers.

Communication between WPF and the embedded PDF.js viewer happens via WebView2's standard interop:
- WPF → JavaScript: `ExecuteScriptAsync` for things like "go to page N" or "trigger print." Minimal use in C5; deferred features will use it more.
- JavaScript → WPF: WebView2's `WebMessageReceived` event. Not used in C5 but the channel is available for future "user clicked annotation," "viewer reports current page number" use cases.

### File URI vs hosted-content for loading

WebView2 supports loading content via three primary mechanisms:

- **File URI (`file:///path/to/file.pdf`):** Direct. Subject to WebView2's file-URI restrictions (no fetch to other origins from a file URI), but PDF.js's bundled viewer is self-contained so this isn't a constraint.
- **Virtual host mapping:** Map a virtual hostname to a local directory. More flexible but adds setup.
- **Streamed content:** Provide content via callbacks. Most flexible, most complex.

C5 uses file URI loading for simplicity. Virtual host mapping or streamed content can be adopted later if cross-origin requirements emerge (e.g., if a future feature needs the viewer to fetch related documents).

## Alternatives Considered

### PdfiumViewer (WPF wrapper over Google's PDFium)

Strong candidate. Rendering fidelity is good (PDFium is what Chrome uses). License compatible (Apache 2.0 over BSD-3-Clause). Lighter deployment footprint (native DLLs only, no runtime requirement). Lower integration complexity than WebView2 + PDF.js.

Rejected on maintenance grounds. The original `pdfiumviewer/PdfiumViewer` repository has not seen sustained activity. Forks exist but none has the same scale of maintenance assurance as Microsoft's WebView2 and Mozilla's PDF.js. For a project intended to be deployed and maintained for years, the maintenance differential is the deciding factor. If a maintained fork or a comparable newer wrapper emerges as the clear successor, future ADR can supersede this decision.

The decision against PdfiumViewer is close — a different weighting of "maintenance assurance vs deployment footprint" would reasonably choose PdfiumViewer. The choice for WebView2 + PDF.js is not "obvious right answer" but "stronger on the criteria this project most values."

### Newer-generation PDFium wrappers (Caly Sharp and similar)

Newer wrappers around PDFium have emerged since the original PdfiumViewer's quiet period. Some show active maintenance. Rejected because none has the multi-year track record that WebView2 and PDF.js have, and "the new well-maintained wrapper" is exactly what PdfiumViewer was at one point. The risk of investing in a small-community library that goes quiet in 2-3 years is real for a project with long maintenance horizons.

If C5 implementation surfaces a newer wrapper with demonstrably strong maintenance and a compelling integration story, the decision can be revisited. The default for this ADR is the lower-risk choice.

### PdfPig with custom WPF rendering

PdfPig is a .NET-native PDF parser. Active maintenance, lightweight dependency, full control over rendering. Rejected because it provides no renderer. Building a PDF renderer in WPF — handling font metrics, vector primitives, image extraction, page composition — is multi-week work and would re-implement what PDF.js does excellently. The cost/benefit is dramatically wrong for a single feature in a single phase.

PdfPig may be the right answer for *non-rendering* PDF operations the project needs in the future (e.g., text extraction for full-text indexing, page-count metadata extraction without opening a viewer). Those use cases are out of Phase 2 scope but worth noting.

### Commercial PDF components (Syncfusion, DevExpress, PSPDFKit, etc.)

Commercial offerings have excellent rendering, complete viewer UIs, and active maintenance. Rejected because the user's preference for this project is open-source dependencies. The commercial-vs-open-source decision was an explicit conversation outcome, not a constraint discovered mid-evaluation. If a future need surfaces that open-source cannot meet (e.g., advanced PDF/A compliance verification, redaction, digital signature support for PDF-embedded signatures), the commercial option can be revisited as a targeted dependency for that specific need.

### Adobe Acrobat Reader as a separate process

Rejected. SPEC §3.4's "no external PDF viewers" rules this out, and operationally launching a separate process for each document view is poor UX.

### Browser-default PDF viewing via "open this file"

Rejected. The same SPEC rule applies, and the UX of "click to open in another app" is not embedded rendering.

## Consequences

### Positive

- Rendering fidelity matches modern browsers; users will not see PDFs rendered noticeably differently than what they see in Edge or Firefox.
- Full viewer feature set (search, print, zoom, page navigation) without integrator-built UI glue.
- Maintenance by Microsoft and Mozilla means future .NET versions and Windows versions are unlikely to break the integration.
- The UserControl wrapper isolates the WebView2 dependency from the rest of the application; future replacement is bounded.
- File-URI loading is simple and well-documented; no virtual host or streamed-content complexity in C5.
- PDF.js's read-only-by-default model aligns with the SPEC requirement for read-only documents inside the app.

### Negative (and accepted)

- **WebView2 Runtime as deployment prerequisite.** Pilot deployments are IT-managed and this is acceptable; air-gapped or non-IT-managed deployments would face friction. Documented in user manual deployment section.
- **WebView2 is "embedded Chromium" architecture for rendering PDFs.** This is heavier than a focused PDF library. Accepted as the cost of the maintenance and fidelity benefits.
- **First-time WebView2 initialization is async** and visibly slower than a synchronous control. Loading indicators in the parent view mask this; user-perceived "the PDF appears" is approximately the same as a native control.
- **PDF.js JavaScript runtime constraints.** Very large PDFs (hundreds of pages with embedded images) may show performance characteristics different from native renderers. Pilot deployment documents are not expected to hit this; if they do, page-level virtualization in PDF.js mitigates.
- **NuGet dependency added to the project.** First third-party UI dependency. Future commits should not interpret this as "the door is open for any UI library"; CLAUDE.md rule 9 still applies, and future dependencies still require their own justification.
- **PDF.js assets in the repo.** ~5MB of JavaScript assets bundled at `assets/pdfviewer/`. Acceptable for a build-artifact dependency; not in source control of the JavaScript itself (treated as vendored release artifacts).
- **WebView2-runtime-missing failure mode** at app launch requires explicit handling (clear error message, link to deployment docs). C5 ships the error message; auto-recovery is out of scope.

### Forward-looking implications

- **PDF.js JavaScript-to-WPF communication channel exists** for future features. Examples that may use it: highlight current page in a doc-navigation sidebar, surface PDF outline as a WPF tree, capture user's last-viewed-page for resume-on-reopen. None of these in C5; the channel is available.
- **Controlled export (signed document with metadata)** in a future commit will use PDF.js's print-to-PDF or backend-generated PDF assembly, not browser download. C5's disable-download configuration aligns with this.
- **Annotation surface** if ever required (currently out of scope) would use PDF.js's annotation layer plus WPF-to-JS interop. Multi-week feature; not Phase 2.

## Implementation Notes

- `Microsoft.Web.WebView2` NuGet version: latest stable at C5 commit time. Lock to a specific version in `EasySynQ.UI.csproj`. Update via deliberate version bumps, not automatic.
- PDF.js version: latest stable LTS at C5 commit time. Bundled as release artifacts; not source-controlled JS. README in `assets/pdfviewer/` documents the bundled version and update procedure.
- `IVaultService.GetVaultFilePathAsync(Guid blobId, CancellationToken ct)` is a new method, flagged at plan time per the C2 scope-creep convention. Returns the on-disk path for the blob without opening a stream. Validates: blob exists, file exists at expected sharded path, hash matches stored hash. The hash validation is the same defensive check that `RetrieveAsync` does — failure modes are the same.
- WebView2 initialization is async and happens on the WPF dispatcher thread. The UserControl handles this via `EnsureCoreWebView2Async()` in its `Loaded` event handler.
- The PDF.js viewer URL is constructed as `file:///{viewerHtmlPath}?file={URI-encoded vault file path}`. URI encoding handles spaces, special characters in vault paths (though the 2-character-sharded SHA-256-hash paths from C2 don't include problematic characters by construction).
- Download is disabled in PDF.js via the `disableForms`, `disableExternalLinks` viewer options and explicit removal of the download button in the viewer UI. Configuration is in the bundled `viewer.html`'s initialization script, not in the WPF-side code.
- Print is enabled; PDF.js's print path uses the browser's print dialog, which WebView2 surfaces as the system print dialog. SPEC §4.5's print-friendly requirement is satisfied.
- Search is enabled; PDF.js's built-in toolbar exposes Ctrl+F.
- The UserControl disposes the WebView2 instance properly on unload. WebView2 instances are not lightweight; leaking them is a real memory cost.
- For testing: WebView2 cannot be instantiated in unit tests (requires a Win32 host window). C5's tests pin the UserControl's public API (DocumentPath property changes trigger navigation; NavigationFailed event fires on bad input), but the actual rendering is verified only in C5's manual smoke. This is consistent with the C4 pattern (UI scaffolding tests pin contract; real interaction smoke happens when wired into actual flows).

## Required Tests

### Unit tests

- `PdfViewerControl` (or equivalent):
  - DocumentPath property change fires PropertyChanged.
  - Setting DocumentPath to an invalid path raises NavigationFailed (verified via mock — actual WebView2 not instantiated).
  - IsViewerReady starts false, becomes true after initialization (mocked).

### Integration tests

- `IVaultService.GetVaultFilePathAsync`:
  - Returns the expected sharded path for an existing blob.
  - Throws when blob row doesn't exist.
  - Throws on hash mismatch (file present but corrupted).
  - Throws on file missing (blob row exists but file deleted).

### Manual smoke (C5)

C5's smoke is the first Phase 2 commit where real user interaction can be meaningfully driven:

- Bundle a known-good PDF (e.g., a sample SOP) into the dev vault for the bootstrap admin's first revision.
- Launch the app; sign in; navigate to the Document detail view (requires C6's UI shell — see note below).
- Confirm the PDF renders, search works (Ctrl+F finds known text in the sample document), print opens the system print dialog, zoom works, page navigation works.
- Confirm download button is absent from the viewer toolbar.
- Verify that a missing WebView2 Runtime produces a clear error message at app launch (uninstall the runtime temporarily on a test machine to validate).

**Smoke ordering note:** C5 lands before C6 (UI shell). The Document detail view that C5 wires the viewer into doesn't fully exist until C6. C5's smoke either uses a temporary test harness window to verify the viewer in isolation, or defers the user-driven smoke to C6 when the full Document detail view is present. The plan can decide which approach fits cleanly. The unit and integration tests pin the contract regardless.

## Subsequent finding — virtual host mapping required (2026-05-16, C6a smoke walk #2)

C6a's first end-to-end smoke surfaced a concrete failure of this ADR's original assumption that file URI loading would suffice. The PDF.js viewer.html loaded and rendered its toolbar but the page-canvas area showed a solid black rectangle: the PDF itself was never loaded. Diagnosis (see C6a SESSION_NOTES) traced the failure to **Chromium's same-origin policy treating every file:// URL as a unique origin**, blocking PDF.js's PDF fetch from the viewer.html's file:// origin to the vault PDF's file:// origin.

The original ADR's §"File URI vs hosted-content for loading" stated:

> File URI (`file:///path/to/file.pdf`): Direct. Subject to WebView2's file-URI restrictions (no fetch to other origins from a file URI), but PDF.js's bundled viewer is self-contained so this isn't a constraint.

That last clause was wrong. The viewer's *assets* (CSS, JS, fonts, locale files) are self-contained — they're loaded by viewer.html itself from the same file:// origin. But the viewer's *core operation* is fetching the target PDF named in the `?file=` query parameter, and that fetch is cross-origin between two distinct file:// origins.

**Decision (amendment):** The control registers two WebView2 virtual hosts at init time and PDFs are addressed through them:

| Virtual host | Maps to | Content |
|---|---|---|
| `https://easysynq-pdfviewer.local/` | `{AppContext.BaseDirectory}/Assets/pdfviewer/web/` | PDF.js viewer assets (the bundled distribution) |
| `https://easysynq-pdfcontent.local/` | the `ContentRoot` dependency property (typically the vault root) | PDF files to display |

Both mappings use `CoreWebView2HostResourceAccessKind.Allow` so PDF.js's fetch across the two virtual hosts succeeds. The control now navigates to `https://easysynq-pdfviewer.local/viewer.html?file={url-encoded content URL}` instead of a file:// URL. Callers (typically a host view-model) translate vault file paths to content URLs via the new static helper `PdfViewerControl.BuildContentUrl(absoluteFilePath, contentRoot)`.

**Shape changes from C5:**

- `PdfViewerControl` gains a `ContentRoot` dependency property. Hosts bind it to whatever folder backs the content-virtual-host mapping (the vault root for Phase 2).
- `BuildViewerUrl` signature changes from `(string viewerHtmlPath, string pdfPath)` to `(string contentUrl)`. The viewer URL is now a constant; the helper only wraps the content URL into `?file=`.
- New static helper `BuildContentUrl(string absoluteFilePath, string contentRoot)` — translates a file system path under `contentRoot` to a `https://easysynq-pdfcontent.local/...` URL.
- `DocumentPath` dependency property's contract changes from "absolute on-disk file path" to "URL the WebView2 navigates to" (typically a content-virtual-host URL).

**Banner for failures.** The amendment also closes the C5-era silent-failure gap: the `NavigationFailed` event already existed but had no subscribers, so PDF-load failures appeared as a silent black page (which is exactly how this very bug masked itself in C6a smoke walk #1). The host view (`DocumentDetailView`) now subscribes to `NavigationFailed` and forwards to the VM's `OnViewerNavigationFailed` handler, which populates `HasViewerLoadError` + `ViewerErrorMessage`. The view binds a red banner above the viewer area to that state.

**Alternatives reconsidered.** The original ADR's §"File URI vs hosted-content for loading" listed three mechanisms (file URI, virtual host mapping, streamed content). Virtual host mapping was deferred as "can be adopted later if cross-origin requirements emerge." Those requirements existed from day one; the ADR's claim that the viewer was "self-contained" misread the situation. The streamed-content alternative remains deferred — virtual host mapping is sufficient for Phase 2 needs and adds no per-request runtime cost.

**Tests updated.** `PdfViewerControlTests.BuildViewerUrl_*` updated to the new shape; new `BuildContentUrl_*` tests cover the path-relative URL construction; `DocumentDetailViewModelTests` updated for `VaultDocumentUrl` (renamed from `VaultFilePath`) and a `ContentRoot` pass-through assertion plus new `OnViewerNavigationFailed_*` tests for the banner state.

**SPEC §5.1 unaffected.** The amendment is a viewer-implementation detail — the spec's "embedded PDF rendering" requirement is unchanged. No SPEC revision bump.

### Viewer-host scope refinement (smoke walk #3, 2026-05-16)

Initial implementation of the virtual-host fix scoped the viewer host mapping at `Assets/pdfviewer/web/` — i.e., the directory containing `viewer.html`. Smoke walk #3 showed the viewer.html loaded (toolbar visible) but PDF.js halted before any content fetch could happen. DevTools console surfaced the actual error:

```
GET https://easysynq-pdfviewer.local/build/pdf.mjs net::ERR_FILE_NOT_FOUND
Uncaught TypeError: Cannot destructure property 'AbortException'
    of 'globalThis.pdfjsLib' as it is undefined. (at viewer.mjs:993)
```

PDF.js 5.7.284's bundled distribution layout is:

```
Assets/pdfviewer/
├── build/        ← pdf.mjs (the library)
└── web/          ← viewer.html (the UI) + viewer.mjs
```

`web/viewer.mjs` imports the library via a sibling-directory relative path (`../build/pdf.mjs`). When the virtual-host mapping was scoped at `web/`, that import resolved to `https://easysynq-pdfviewer.local/build/pdf.mjs` — a path OUTSIDE the mapped folder, returning 404 and leaving `pdfjsLib` undefined. PDF.js's destructure threw, init halted, and the content fetch never started.

**Corrected scope:** map the viewer host at the distribution's PARENT directory:

| Setting | Before | After |
|---|---|---|
| Virtual-host folder | `Assets/pdfviewer/web/` | `Assets/pdfviewer/` |
| Viewer URL | `https://easysynq-pdfviewer.local/viewer.html` | `https://easysynq-pdfviewer.local/web/viewer.html` |

With this scope the viewer's own assets (`/web/...`) AND the library it imports (`/build/...`) both resolve under the same virtual host.

**Version-bump verification step (extends the C5 PDF.js version-bump lesson).** At every PDF.js version bump, verify the distribution's top-level directory layout. If a future PDF.js version reorganizes (e.g., single-bundle delivery, renamed sibling directories), the `ViewerAssetsRelativePath` constant and the `web/` segment in `ViewerHtmlUrl` need revisiting. Add this to the PDF.js bump checklist alongside the selector-ID re-verification step.

### Sub-resource failure surfaces — `WebResourceResponseReceived` (smoke walk #3, 2026-05-16)

The original banner wiring subscribed to `CoreWebView2.NavigationCompleted` which fires for **top-level navigations only**. PDF.js's fetch of the content URL is a **sub-resource request** issued by JavaScript inside the loaded page — `NavigationCompleted` never fires for that. The cross-origin bug in smoke walks #1 and #2, the viewer-host scope bug in smoke walk #3, and any future failure mode targeting the content host (vault file deleted, hash mismatch, content host misconfigured) all share this characteristic: they happen below the navigation layer.

The structural fix subscribes additionally to `CoreWebView2.WebResourceResponseReceived`, filtered by URL prefix matching the content virtual host. Non-2xx responses route to `RaiseNavigationFailed` with reason `"ContentFetchFailed (HTTP {status})"`. The two events together cover both layers of WebView2's failure surface: outer-navigation failures via `NavigationCompleted.IsSuccess == false`; sub-resource failures via `WebResourceResponseReceived`.

The viewer-host's own sub-resource failures (PDF.js library imports, viewer assets) are intentionally NOT routed to the banner — those failures prevent the viewer from initializing at all, and the right diagnostic for them is the developer log / version-bump verification pass, not a runtime user-facing banner.

**The banner is fed from THREE input wires.** As of the smoke-walk-#5 verification finding, the viewer-error banner consolidates failures from three host-side surfaces — one per covered layer in the cascade-init failure model:

| # | Wire | Layer | Covers |
|---|---|---|---|
| 1 | `PdfViewerControl.NavigationFailed` from `NavigationCompleted.IsSuccess == false` | Layer 1 (outer navigation) | viewer.html load failures, scheme errors |
| 2 | `PdfViewerControl.NavigationFailed` from `WebResourceResponseReceived` non-2xx on the content host | Layer 2 (sub-resource fetches) | content PDF 404 / CORS block / server error |
| 3 | `DocumentDetailViewModel.LoadAsync` try/catch around vault I/O → direct call to `OnViewerNavigationFailed` | Layer 0 (VM-side pre-viewer load) | `IVaultService.GetVaultFilePathAsync` throws (file missing, hash mismatch, blob row gone) |

Layer 0 was the post-amendment fourth wire. Without it, `FileNotFoundException` / `InvalidDataException` / `KeyNotFoundException` thrown from `GetVaultFilePathAsync` escaped through the async-void `Loaded` handler to the WPF dispatcher's last-resort handler — wrong UX shape for a per-document load failure. The catch is narrow to those three documented vault failure modes; unrelated exceptions (programming errors, infrastructure failures) still escape to the dispatcher so they don't masquerade as vault-content issues. See `DocumentDetailViewModelTests.LoadAsync_VaultFileMissing_*` / `LoadAsync_VaultHashMismatch_*` / `LoadAsync_VaultBlobRowMissing_*` / `LoadAsync_UnexpectedException_StillEscapes` for the pinned behavior.

Layer 3 remains deliberately uncovered — see the HOSTED_VIEWER_ORIGINS section below.

### PDF.js HOSTED_VIEWER_ORIGINS patch (smoke walk #4, 2026-05-16)

Smoke walk #4 surfaced a **Layer 3** failure — PDF.js's own JavaScript-internal validation rejecting the content URL before any fetch was attempted. DevTools console:

```
Uncaught (in promise) Error: file origin does not match viewer's
  at validateFileURL (viewer.mjs:19512:16)
```

`validateFileURL` is PDF.js's open-redirect guard for Mozilla's hosted viewer at `mozilla.github.io`. It rejects PDF URLs whose origin differs from the viewer's origin, with a hardcoded `HOSTED_VIEWER_ORIGINS` allowlist of Mozilla-known origins (`"null"`, `"http://mozilla.github.io"`, `"https://mozilla.github.io"`). The amendment's two-virtual-host design (`easysynq-pdfviewer.local` + `easysynq-pdfcontent.local`) is exactly the cross-origin pattern the guard rejects.

**Failure cascade model.** This is the third distinct layer at which a viewer load can fail in our deployment:

| Layer | Surface | What we observed |
|---|---|---|
| 1 — Outer navigation | `CoreWebView2.NavigationCompleted.IsSuccess` | The original (pre-amendment) cross-origin file:// failure — viewer.html itself failed to load. Caught by the original NavigationFailed wiring. |
| 2 — Sub-resource fetches | `CoreWebView2.WebResourceResponseReceived` (status code) | Walk #3's viewer-host scope bug (build/pdf.mjs → 404); a hypothetical future case of the content PDF being missing or CORS-blocked. Caught by the smoke-walk-#3 `WebResourceResponseReceived` expansion. |
| 3 — JS-internal validation / errors | PDF.js's `eventBus` events surfaced via WebView2 ↔ JS bridge | Walk #4's validateFileURL rejection — no fetch happens, no WebView2 host-side event fires. **Not covered by the C6a banner** by design (see below). |

**Decision — Option A (patch viewer.mjs's HOSTED_VIEWER_ORIGINS).** A single-line edit to the Set literal adds `"https://easysynq-pdfviewer.local"` as a fourth allowed origin. PDF.js then treats our viewer as a hosted-viewer and skips the validation.

**Why Option A over Option B (single virtual host via WebResourceRequested handler).** WebView2's `SetVirtualHostNameToFolderMapping` is one-folder-per-hostname; a single-virtual-host design would require a custom `WebResourceRequested` event handler routing requests by URL prefix to different folders. The hidden cost is **Range request support**: large PDFs (100+ page customer specifications are realistic in a QMS deployment) trigger HTTP Range requests so PDF.js can stream pages on demand. Implementing Range correctly involves RFC 7233 edge cases (multi-range, suffix-length, past-EOF, If-Range) that WebView2's native fetch pipeline already handles. Option B would reproduce existing WebView2 infrastructure with our own bugs in 50-100 lines of handler code we own forever. Option A is one line in a vendored file with a documented per-bump verification step.

**Maintenance shape.** The patch adds one entry to the PDF.js version-bump checklist (alongside the existing toolbar-ID re-verification from C5's lesson and the easysynq-overrides.css selector re-verification). See `Assets/pdfviewer/README.md` for the full bump procedure.

**Layer 3 coverage decision — out of scope for C6a.** With Option A in place, the only expected Layer 3 case (validateFileURL) no longer fires in normal operation. The banner therefore covers Layers 1 + 2 only. If a future feature requires Layer 3 coverage — e.g., catching PDF parse errors on malformed PDFs, surfacing PDF.js's internal "document failed to load" events — the right shape is option (i) from the smoke-walk-#3 triage: a JavaScript-to-host bridge via WebView2's `AddScriptToExecuteOnDocumentCreatedAsync` + `WebMessageReceived`, subscribing to PDF.js's `eventBus.on('documenterror', ...)` events. Not done now; the cascade-init-failure model is what we'd consult before deciding to add it.

## References

- `docs/SPEC.md` §3.4 (No external PDF viewers — context for the in-process rendering choice); §4.5 (Print-friendly requirement); §5.1 (Document Controller's embedded-viewer requirement)
- ADR 0001 (Lean stack discipline — this ADR justifies the first UI third-party dependency)
- ADR 0008 (Phase 2 scope — C5 paired with this ADR)
- `docs/SESSION_NOTES.md` 2026-05-15 (Phase 2 C4) — handoff entry that flagged C5 as the next pickup with the library-choice decision pending
- `docs/SESSION_NOTES.md` 2026-05-16 (Phase 2 C6a smoke walk #2 + #3) — where the cross-origin finding surfaced and the virtual-host fix landed
