# ADR 0010 — PDF Viewer Dependency (WebView2 + PDF.js)

**Status:** Accepted
**Date:** 2026-05-15 (Proposed), 2026-05-15 (Accepted)
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

## References

- `docs/SPEC.md` §3.4 (No external PDF viewers — context for the in-process rendering choice); §4.5 (Print-friendly requirement); §5.1 (Document Controller's embedded-viewer requirement)
- ADR 0001 (Lean stack discipline — this ADR justifies the first UI third-party dependency)
- ADR 0008 (Phase 2 scope — C5 paired with this ADR)
- `docs/SESSION_NOTES.md` 2026-05-15 (Phase 2 C4) — handoff entry that flagged C5 as the next pickup with the library-choice decision pending
