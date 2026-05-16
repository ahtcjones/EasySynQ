# PDF.js Bundled Distribution

**Bundled version:** PDF.js 5.7.284
**Source:** [Mozilla pdf.js v5.7.284 release](https://github.com/mozilla/pdf.js/releases/tag/v5.7.284)
**Distribution artifact:** `pdfjs-5.7.284-dist.zip`
**License:** Apache 2.0 (see `LICENSE` in this folder)

---

This folder contains the prebuilt PDF.js distribution bundled with the EasySynQ app for embedded PDF viewing per ADR 0010 (C5). The `build/` and `web/` subdirectories are vendored release artifacts — verbatim from Mozilla's distribution zip — and are not maintained as source within this repository.

The `EasySynQ.UI.csproj` Content Include copies this whole folder to the build output; at runtime, `PdfViewerControl` constructs `file:///` URIs against `AppContext.BaseDirectory + "Assets/pdfviewer/web/viewer.html"`.

## EasySynQ-specific modifications

Two files in this folder are EasySynQ additions, NOT part of the upstream PDF.js distribution:

1. **`web/easysynq-overrides.css`** — hides the download and open-file toolbar affordances per SPEC §5.1's read-only-inside-the-app requirement. Targets PDF.js 5.7.284's specific element IDs; see the file's header for the IDs and rationale.

2. **`web/viewer.html`** is patched with **one line** inserted just before the closing `</head>` tag:

   ```html
   <link rel="stylesheet" href="easysynq-overrides.css" />
   ```

   The exact insertion point is documented in the comment above the inserted line. Everything else in `viewer.html` is verbatim from the upstream distribution.

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
