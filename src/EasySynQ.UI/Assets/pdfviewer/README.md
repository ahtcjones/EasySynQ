# PDF.js Bundled Distribution

**Bundled version:** PDF.js 5.7.284
**Source:** [Mozilla pdf.js v5.7.284 release](https://github.com/mozilla/pdf.js/releases/tag/v5.7.284)
**Distribution artifact:** `pdfjs-5.7.284-dist.zip`
**License:** Apache 2.0 (see `LICENSE` in this folder)

---

This folder contains the prebuilt PDF.js distribution bundled with the EasySynQ app for embedded PDF viewing per ADR 0010 (C5). The `build/` and `web/` subdirectories are vendored release artifacts — verbatim from Mozilla's distribution zip — and are not maintained as source within this repository.

The `EasySynQ.UI.csproj` Content Include copies this whole folder to the build output; at runtime, `PdfViewerControl` constructs `file:///` URIs against `AppContext.BaseDirectory + "Assets/pdfviewer/web/viewer.html"`.

## EasySynQ-specific modifications

Three files in this folder are EasySynQ-modified, NOT verbatim from the upstream PDF.js distribution:

1. **`web/easysynq-overrides.css`** — hides the download and open-file toolbar affordances per SPEC §5.1's read-only-inside-the-app requirement. Targets PDF.js 5.7.284's specific element IDs; see the file's header for the IDs and rationale.

2. **`web/viewer.html`** is patched with **one line** inserted just before the closing `</head>` tag:

   ```html
   <link rel="stylesheet" href="easysynq-overrides.css" />
   ```

   The exact insertion point is documented in the comment above the inserted line. Everything else in `viewer.html` is verbatim from the upstream distribution.

3. **`web/viewer.mjs`** is patched with **one entry** added to the `HOSTED_VIEWER_ORIGINS` Set literal at the top of the `validateFileURL` IIFE (around line 19499 in 5.7.284):

   ```javascript
   // Original (upstream):
   const HOSTED_VIEWER_ORIGINS = new Set(["null", "http://mozilla.github.io", "https://mozilla.github.io"]);
   // Patched (EasySynQ — adds our viewer virtual host):
   const HOSTED_VIEWER_ORIGINS = new Set(["null", "http://mozilla.github.io", "https://mozilla.github.io", "https://easysynq-pdfviewer.local"]);
   ```

   **Why:** PDF.js's `validateFileURL` rejects PDF URLs whose origin differs from the viewer's origin — an open-redirect guard for Mozilla's hosted viewer at `mozilla.github.io`. ADR 0010's amendment maps the viewer assets and the vault to **two different virtual hosts** (`easysynq-pdfviewer.local` and `easysynq-pdfcontent.local`) — a deliberate cross-origin pattern. Adding our viewer origin to the allowlist tells PDF.js the cross-origin pattern is intentional in our deployment context (an in-process embedded viewer with controlled local resources, not an internet-hosted viewer at risk of open-redirect abuse).

   **Why not a config flag.** `HOSTED_VIEWER_ORIGINS` is a `const` inside an IIFE block; it is not exposed on `globalThis` or via any viewer configuration API. There is no script-level override path (unlike the CSS overrides). The patch must edit `viewer.mjs` directly.

   **Why not a single virtual host instead.** WebView2's `SetVirtualHostNameToFolderMapping` is one-folder-per-hostname; the viewer assets and the vault live at completely separate disk locations (`AppContext.BaseDirectory/Assets/pdfviewer/` and `%LOCALAPPDATA%/EasySynQ/vault/`); collapsing to one host would require either reorganizing disk layout (conflates concerns) or implementing a custom `WebResourceRequested` handler (50-100 lines including Range request support — significantly more code we own forever). See ADR 0010 §"PDF.js HOSTED_VIEWER_ORIGINS patch (smoke walk #4)" for the full reasoning.

## Updating to a newer PDF.js version

When bumping PDF.js to a newer release:

1. Download the new `pdfjs-X.Y.Z-dist.zip` from the [PDF.js releases page](https://github.com/mozilla/pdf.js/releases).
2. Delete this folder's `build/` and `web/` contents (keep `LICENSE` if the new version updates it).
3. Extract the new zip into this folder (replacing `build/`, `web/`, `LICENSE`).
4. **Re-apply the viewer.html `<link>` patch** — search for the existing `<link rel="stylesheet" href="viewer.css" />` line in `web/viewer.html` and add the EasySynQ override `<link>` per the comment block in `easysynq-overrides.css`.
5. **Re-verify the override CSS selectors.** Open the new `web/viewer.html` and search for the toolbar element IDs the override targets:
   - `#downloadButton` (was `#download` in PDF.js 4.x; renamed in 5.x)
   - `#secondaryDownload` (overflow-menu download)
   - `#secondaryOpenFile` (overflow-menu open-file)

   If Mozilla has renamed any of these, update `easysynq-overrides.css` to match. Run the app and confirm the download/open-file affordances are hidden.
6. **Re-apply the HOSTED_VIEWER_ORIGINS patch in `viewer.mjs`.** Search the file for `HOSTED_VIEWER_ORIGINS = new Set(`. The upstream literal contains the three Mozilla origins (`"null"`, `"http://mozilla.github.io"`, `"https://mozilla.github.io"`). Add `"https://easysynq-pdfviewer.local"` as a fourth entry. Verify the surrounding `validateFileURL` function shape still matches what the patch assumes — if Mozilla refactors the validation logic (e.g., extracts the allowlist to a config import, removes the IIFE block, renames the function), the patch may need a different form. If you cannot find `HOSTED_VIEWER_ORIGINS` at all in the new version, validateFileURL may have been removed entirely; smoke a fresh PDF render to confirm no Layer-3 rejection surfaces.
7. Update this README's "Bundled version" header.
8. Commit. The commit message should call out the version bump and any selector or patch changes that surfaced during steps 5-6.

## Updating to a newer PDF.js version

When bumping PDF.js to a newer release:

1. Download the new `pdfjs-X.Y.Z-dist.zip` from the [PDF.js releases page](https://github.com/mozilla/pdf.js/releases).
2. Delete this folder's `build/` and `web/` contents (keep `LICENSE` if the new version updates it).
3. Extract the new zip into this folder (replacing `build/`, `web/`, `LICENSE`).
4. **Re-apply the viewer.html `<link>` patch** — search for the existing `<link rel="stylesheet" href="viewer.css" />` line in `web/viewer.html` and add the EasySynQ override `<link>` per the comment block in `easysynq-overrides.css`.
5. **Re-verify the override CSS selectors.** Open the new `web/viewer.html` and search for the toolbar element IDs the override targets:
   - `#downloadButton` (was `#download` in PDF.js 4.x; renamed in 5.x)
   - `#secondaryDownload` (overflow-menu download)
   - `#secondaryOpenFile` (overflow-menu open-file)

   If Mozilla has renamed any of these, update `easysynq-overrides.css` to match. Run the app and confirm the download/open-file affordances are hidden.
6. Update this README's "Bundled version" header.
7. Commit. The commit message should call out the version bump and any selector changes that surfaced during step 5.

## Why this is vendored, not fetched

The deployment context is air-gappable manufacturing facilities with IT-managed installs. Build-time downloads or runtime CDN fetches add a fragility this product cannot accept. Vendoring the distribution in source control means the build is self-contained; a clone-and-build at any future date produces a reproducible artifact regardless of GitHub's availability.

The repository-size cost (~5 MB across the bundled assets) is documented in ADR 0010 §"Negative" and is accepted as the price of deployment self-sufficiency.
