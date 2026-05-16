using System.IO;
using System.Windows;
using System.Windows.Controls;

using Microsoft.Web.WebView2.Core;

namespace EasySynQ.UI.Documents.Controls;

/// <summary>
/// WPF UserControl wrapping a <c>Microsoft.Web.WebView2.Wpf.WebView2</c>
/// instance configured to host the bundled PDF.js viewer (ADR 0010 C5,
/// amended 2026-05-16). Loads PDFs into the embedded viewer via the
/// <see cref="DocumentPath"/> dependency property; surfaces failures
/// through the <see cref="NavigationFailed"/> event; signals
/// initialization completion through <see cref="IsViewerReady"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Lifecycle.</b> The WebView2 instance initializes asynchronously
/// on the WPF dispatcher when the control's <c>Loaded</c> event fires.
/// Setting <see cref="DocumentPath"/> before initialization is safe —
/// the queued path is navigated as soon as initialization completes.
/// Setting it after initialization triggers an immediate navigation.
/// </para>
/// <para>
/// <b>Virtual host mapping (ADR 0010 amendment, 2026-05-16).</b>
/// PDF.js's viewer.html fetches the target PDF as a separate HTTP
/// request after it loads. Chromium's same-origin policy treats every
/// <c>file://</c> URL as a unique origin, so a viewer loaded from
/// <c>file://</c> cannot fetch a PDF from another <c>file://</c> path.
/// To work around this WebView2 idiom, the control registers two
/// virtual hosts at init time:
/// <list type="bullet">
///   <item><c>https://easysynq-pdfviewer.local/</c> →
///   <c>{AppContext.BaseDirectory}/Assets/pdfviewer/web/</c>
///   (the bundled PDF.js viewer assets).</item>
///   <item><c>https://easysynq-pdfcontent.local/</c> →
///   <see cref="ContentRoot"/> (the folder containing PDF files to
///   display, typically the vault root).</item>
/// </list>
/// Both registrations use
/// <see cref="CoreWebView2HostResourceAccessKind.Allow"/> so PDF.js's
/// fetch across the two virtual hosts succeeds. The
/// <see cref="DocumentPath"/> dependency property holds a content URL
/// (typically <c>https://easysynq-pdfcontent.local/{shard}/{hash}.pdf</c>)
/// constructed by the caller via <see cref="BuildContentUrl"/>.
/// </para>
/// <para>
/// <b>Asset location.</b> The PDF.js viewer.html is expected at
/// <c>{AppContext.BaseDirectory}/Assets/pdfviewer/web/viewer.html</c>
/// — populated by the <see cref="EasySynQ.UI"/> project's csproj
/// Content Include for <c>Assets/pdfviewer/**/*</c>. If the assets
/// directory is missing at runtime, init surfaces
/// <c>Reason = "ViewerAssetsMissing"</c> on
/// <see cref="NavigationFailed"/>.
/// </para>
/// <para>
/// <b>Threading.</b> WebView2 must be created on the WPF dispatcher;
/// constructing the control off-thread fails. Tests therefore exercise
/// the dependency-property surface, event-args shape, and the static
/// <see cref="BuildViewerUrl"/> / <see cref="BuildContentUrl"/>
/// helpers only — they do not instantiate the control as a live UI
/// element. Real viewer behavior is verified in C6's smoke when the
/// Document detail view hosts the control.
/// </para>
/// <para>
/// <b>What this control does NOT do.</b> Wiring into the Document
/// detail view's lifecycle — current-revision tracking, vault-blob
/// resolution via <c>IVaultService.GetVaultFilePathAsync</c>, the
/// no-blob placeholder shape — lives in the view-model layer. This
/// control is a single-purpose viewer surface: hand it a content URL,
/// it shows the PDF.
/// </para>
/// </remarks>
public partial class PdfViewerControl : UserControl
{
    private const string ViewerVirtualHost = "easysynq-pdfviewer.local";
    private const string ContentVirtualHost = "easysynq-pdfcontent.local";

    // The viewer-host mapping must scope at the PDF.js distribution's
    // PARENT directory (Assets/pdfviewer/), not at the viewer
    // subdirectory (Assets/pdfviewer/web/). PDF.js's viewer.mjs
    // imports the library via a sibling-path relative URL
    // (../build/pdf.mjs). If the mapping is scoped at .../web/, that
    // import resolves to https://easysynq-pdfviewer.local/build/...
    // which falls OUTSIDE the mapped folder and 404s with
    // ERR_FILE_NOT_FOUND. The downstream symptom is "pdfjsLib is
    // undefined" inside viewer.mjs's destructure — PDF.js halts before
    // any content fetch.
    //
    // Verify at every PDF.js version bump: confirm the distribution's
    // top-level directory layout (build/ + web/ in the bundled
    // distribution at 5.7.284) still has the viewer importing the
    // library via a sibling-path resolve. If a future PDF.js version
    // reorganizes the layout (single bundle / different sibling
    // names), this constant set needs revisiting.
    private const string ViewerHtmlUrl = $"https://{ViewerVirtualHost}/web/viewer.html";
    private const string ViewerAssetsRelativePath = @"Assets\pdfviewer";

    /// <summary>Backing storage for <see cref="DocumentPath"/>.</summary>
    public static readonly DependencyProperty DocumentPathProperty =
        DependencyProperty.Register(
            nameof(DocumentPath),
            typeof(string),
            typeof(PdfViewerControl),
            new PropertyMetadata(defaultValue: null, OnDocumentPathChanged));

    /// <summary>Backing storage for <see cref="ContentRoot"/>.</summary>
    public static readonly DependencyProperty ContentRootProperty =
        DependencyProperty.Register(
            nameof(ContentRoot),
            typeof(string),
            typeof(PdfViewerControl),
            new PropertyMetadata(defaultValue: null, OnContentRootChanged));

    /// <summary>Backing storage for <see cref="IsViewerReady"/>.</summary>
    public static readonly DependencyProperty IsViewerReadyProperty =
        DependencyProperty.Register(
            nameof(IsViewerReady),
            typeof(bool),
            typeof(PdfViewerControl),
            new PropertyMetadata(defaultValue: false));

    /// <summary>
    /// Content URL to display in the embedded viewer. Typically a
    /// <c>https://easysynq-pdfcontent.local/{relative-path}</c> URL
    /// constructed via <see cref="BuildContentUrl"/>; the WebView2
    /// resolves it through the content virtual-host mapping. Setting
    /// triggers navigation (immediately if the viewer is ready;
    /// queued otherwise). Setting to <see langword="null"/> or empty
    /// navigates to a blank state.
    /// </summary>
    public string? DocumentPath
    {
        get => (string?)GetValue(DocumentPathProperty);
        set => SetValue(DocumentPathProperty, value);
    }

    /// <summary>
    /// Absolute path to the folder whose contents should be addressable
    /// via the content virtual host (<c>https://easysynq-pdfcontent.local/</c>).
    /// For C6a this is the vault root. Must be set before the control
    /// initializes (typically via XAML binding to the host VM's
    /// equivalent property). Changes after init are honored — the new
    /// mapping replaces the prior one on the underlying WebView2.
    /// </summary>
    public string? ContentRoot
    {
        get => (string?)GetValue(ContentRootProperty);
        set => SetValue(ContentRootProperty, value);
    }

    /// <summary>
    /// <see langword="true"/> once the WebView2 instance has finished
    /// async initialization and is ready to navigate. XAML bindings
    /// (loading indicators, etc.) can observe this. Private setter —
    /// only the control mutates.
    /// </summary>
    public bool IsViewerReady
    {
        get => (bool)GetValue(IsViewerReadyProperty);
        private set => SetValue(IsViewerReadyProperty, value);
    }

    /// <summary>
    /// Raised when navigation to a PDF fails — viewer assets missing,
    /// WebView2 init failed, or WebView2's navigation pipeline
    /// reported a non-success result. Consumers (typically the host
    /// view-model) surface
    /// <see cref="PdfViewerNavigationFailedEventArgs.Reason"/> in a
    /// banner so failures don't appear as a silent black page.
    /// </summary>
    public event EventHandler<PdfViewerNavigationFailedEventArgs>? NavigationFailed;

    /// <summary>Constructs the control.</summary>
    public PdfViewerControl()
    {
        InitializeComponent();
        Loaded += OnLoadedAsync;
        Unloaded += OnUnloaded;
    }

    private async void OnLoadedAsync(object sender, RoutedEventArgs e)
    {
        // EnsureCoreWebView2Async initializes the underlying WebView2
        // runtime / browser process. First-call latency is
        // user-visible (~100ms-1s on a cold start); subsequent calls
        // on the same process complete near-instantly. We do NOT
        // detect "WebView2 Runtime missing" here — that's an app-
        // launch-time check in App.xaml.cs (ADR 0010 C5 + plan §A6).
        // If the runtime IS missing and somehow we got past startup,
        // EnsureCoreWebView2Async throws and we surface as
        // "WebView2InitFailed" via NavigationFailed.
        try
        {
            await WebView.EnsureCoreWebView2Async();
            WebView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            // Sub-resource failure detection (smoke walk #3 lesson):
            // NavigationCompleted catches outer-navigation failures
            // only. PDF.js's fetch of the content URL is a
            // sub-resource request inside the renderer; its failure
            // doesn't surface through NavigationCompleted. The
            // WebResourceResponseReceived event covers that gap by
            // firing for every response in the WebView2 (including
            // sub-resources).
            WebView.CoreWebView2.WebResourceResponseReceived += OnContentResourceResponseReceived;

            // Register the viewer-assets virtual host. Without this
            // mapping, the viewer.html cannot be loaded via the
            // https:// scheme and PDF.js's PDF-fetch will fall under
            // the file:// origin restriction.
            var viewerAssetsRoot = Path.Combine(
                AppContext.BaseDirectory, ViewerAssetsRelativePath);
            if (!Directory.Exists(viewerAssetsRoot))
            {
                RaiseNavigationFailed(DocumentPath, "ViewerAssetsMissing", exception: null);
                return;
            }
            WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                ViewerVirtualHost,
                viewerAssetsRoot,
                CoreWebView2HostResourceAccessKind.Allow);

            // Register the content virtual host if ContentRoot has been
            // supplied (via XAML binding or direct assignment). If it
            // arrives later, OnContentRootChanged registers it.
            RegisterContentHostIfReady();

            IsViewerReady = true;

            // If a DocumentPath was set before Loaded fired, navigate
            // to it now.
            if (!string.IsNullOrEmpty(DocumentPath))
            {
                NavigateToDocument(DocumentPath);
            }
        }
        catch (Exception ex)
        {
            RaiseNavigationFailed(DocumentPath, "WebView2InitFailed", ex);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // Detach event handlers and dispose the WebView2 instance.
        // WebView2 instances are not lightweight; leaking them is a
        // real memory cost, especially as the Document detail view
        // gets navigated to and away from across a session.
        if (WebView.CoreWebView2 is not null)
        {
            WebView.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
            WebView.CoreWebView2.WebResourceResponseReceived -= OnContentResourceResponseReceived;
        }
        WebView.Dispose();
    }

    private static void OnDocumentPathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (PdfViewerControl)d;
        var newPath = e.NewValue as string;

        // If the viewer isn't ready yet, the OnLoaded continuation
        // will pick up the current value of DocumentPath after init
        // completes. No queuing needed beyond that.
        if (!control.IsViewerReady)
        {
            return;
        }

        if (string.IsNullOrEmpty(newPath))
        {
            // Navigate to about:blank so any previously-loaded PDF is
            // unloaded; the UserControl shows an empty page rather
            // than the prior PDF.
            control.WebView.CoreWebView2?.Navigate("about:blank");
            return;
        }

        control.NavigateToDocument(newPath);
    }

    private static void OnContentRootChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (PdfViewerControl)d;
        // Only relevant once the WebView2 has initialized. OnLoaded
        // also calls RegisterContentHostIfReady so a value set before
        // init is picked up at the right moment.
        if (control.IsViewerReady)
        {
            control.RegisterContentHostIfReady();
        }
    }

    private void RegisterContentHostIfReady()
    {
        if (WebView.CoreWebView2 is null)
        {
            return;
        }
        var root = ContentRoot;
        if (string.IsNullOrEmpty(root))
        {
            return;
        }

        // WebView2's SetVirtualHostNameToFolderMapping replaces any
        // prior registration for the same host name, so re-registering
        // when ContentRoot changes after init is safe and idempotent.
        WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            ContentVirtualHost,
            root,
            CoreWebView2HostResourceAccessKind.Allow);
    }

    private void NavigateToDocument(string contentUrl)
    {
        if (WebView.CoreWebView2 is null)
        {
            return;
        }

        var url = BuildViewerUrl(contentUrl);
        WebView.CoreWebView2.Navigate(url);
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {
            RaiseNavigationFailed(DocumentPath, "NavigationCompletedWithError", exception: null);
        }
    }

    private void OnContentResourceResponseReceived(
        object? sender,
        CoreWebView2WebResourceResponseReceivedEventArgs e)
    {
        // Sub-resource visibility. WebView2 fires this event for every
        // response the renderer receives — viewer assets, PDF.js
        // module imports, the content PDF itself, anything else the
        // page fetches. We only care about failures targeting the
        // content host (the PDF the viewer is trying to load). The
        // viewer-host's own resources are not surfaced — those
        // failures would prevent the viewer from even initializing,
        // and the diagnostic value of "the viewer assets are
        // misconfigured" is best surfaced via developer logs / a
        // version-bump verification pass, not a runtime banner.
        var uri = e.Request.Uri;
        if (!uri.StartsWith(
                $"https://{ContentVirtualHost}/",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var status = e.Response.StatusCode;
        if (status is >= 200 and < 300)
        {
            return; // success
        }

        RaiseNavigationFailed(
            DocumentPath,
            $"ContentFetchFailed (HTTP {status})",
            exception: null);
    }

    private void RaiseNavigationFailed(string? attemptedPath, string reason, Exception? exception)
    {
        NavigationFailed?.Invoke(
            this,
            new PdfViewerNavigationFailedEventArgs(attemptedPath, reason, exception));
    }

    /// <summary>
    /// Constructs the PDF.js viewer navigation URL for the supplied
    /// content URL. Internal so the test surface can pin the URL
    /// shape against accidental drift.
    /// </summary>
    /// <param name="contentUrl">The URL of the PDF to display.
    /// Typically a <c>https://easysynq-pdfcontent.local/...</c> URL
    /// constructed via <see cref="BuildContentUrl"/>; tests may pass
    /// arbitrary URL shapes to verify the encoding.</param>
    /// <returns>The viewer URL the WebView2 navigates to.</returns>
    internal static string BuildViewerUrl(string contentUrl)
    {
        ArgumentException.ThrowIfNullOrEmpty(contentUrl);
        return $"{ViewerHtmlUrl}?file={Uri.EscapeDataString(contentUrl)}";
    }

    /// <summary>
    /// Builds a content-virtual-host URL for the supplied
    /// <paramref name="absoluteFilePath"/>, computed relative to
    /// <paramref name="contentRoot"/>. Callers (typically the host
    /// view-model) use this to translate a vault file path into a
    /// URL that the control can hand to its embedded viewer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The control owns the content virtual-host naming convention so
    /// the host doesn't have to know what the host name is. As long
    /// as the caller passes the same <paramref name="contentRoot"/>
    /// they bind into <see cref="ContentRoot"/>, the returned URL
    /// resolves correctly through the registered mapping.
    /// </para>
    /// <para>
    /// Path-segment URL-encoding is NOT applied — vault files use
    /// hex-only sharded names (e.g., <c>2a/2a6ea32f...pdf</c>) which
    /// contain no characters requiring encoding. Future content
    /// sources with non-ASCII names would need explicit encoding;
    /// flagged here for the next consumer.
    /// </para>
    /// </remarks>
    /// <param name="absoluteFilePath">Absolute path to the file the
    /// caller wants to display. Must live under
    /// <paramref name="contentRoot"/>.</param>
    /// <param name="contentRoot">Absolute path to the folder mapped
    /// to the content virtual host (the value set on
    /// <see cref="ContentRoot"/>).</param>
    /// <returns>A URL of the form
    /// <c>https://easysynq-pdfcontent.local/{relative-path}</c>.</returns>
    public static string BuildContentUrl(string absoluteFilePath, string contentRoot)
    {
        ArgumentException.ThrowIfNullOrEmpty(absoluteFilePath);
        ArgumentException.ThrowIfNullOrEmpty(contentRoot);

        var relativePath = Path.GetRelativePath(contentRoot, absoluteFilePath)
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');

        return $"https://{ContentVirtualHost}/{relativePath}";
    }
}
