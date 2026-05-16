using System.IO;
using System.Windows;
using System.Windows.Controls;

using Microsoft.Web.WebView2.Core;

namespace EasySynQ.UI.Documents.Controls;

/// <summary>
/// WPF UserControl wrapping a <c>Microsoft.Web.WebView2.Wpf.WebView2</c>
/// instance configured to host the bundled PDF.js viewer (ADR 0010 C5).
/// Loads PDFs by file URI into the embedded viewer via the
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
/// <b>Asset location.</b> The PDF.js viewer.html is expected at
/// <c>{AppContext.BaseDirectory}/Assets/pdfviewer/web/viewer.html</c>
/// — populated by the <see cref="EasySynQ.UI"/> project's csproj
/// Content Include for <c>Assets/pdfviewer/**/*</c>. If the assets
/// are missing at runtime, navigation surfaces
/// <c>Reason = "ViewerAssetsMissing"</c> on
/// <see cref="NavigationFailed"/>.
/// </para>
/// <para>
/// <b>Threading.</b> WebView2 must be created on the WPF dispatcher;
/// constructing the control off-thread fails. Tests therefore exercise
/// the dependency-property surface and event-args shape only — they
/// do not instantiate the control as a live UI element. Real viewer
/// behavior is verified in C6's smoke when the Document detail view
/// hosts the control.
/// </para>
/// <para>
/// <b>What this control does NOT do.</b> Wiring into the Document
/// detail view's lifecycle — current-revision tracking, vault-blob
/// resolution via <c>IVaultService.GetVaultFilePathAsync</c>, the
/// no-blob placeholder shape — lives in C6's view-model layer. This
/// control is a single-purpose viewer surface: hand it a path, it
/// shows the PDF.
/// </para>
/// </remarks>
public partial class PdfViewerControl : UserControl
{
    private const string ViewerRelativePath = @"Assets\pdfviewer\web\viewer.html";

    /// <summary>Backing storage for <see cref="DocumentPath"/>.</summary>
    public static readonly DependencyProperty DocumentPathProperty =
        DependencyProperty.Register(
            nameof(DocumentPath),
            typeof(string),
            typeof(PdfViewerControl),
            new PropertyMetadata(defaultValue: null, OnDocumentPathChanged));

    /// <summary>Backing storage for <see cref="IsViewerReady"/>.</summary>
    public static readonly DependencyProperty IsViewerReadyProperty =
        DependencyProperty.Register(
            nameof(IsViewerReady),
            typeof(bool),
            typeof(PdfViewerControl),
            new PropertyMetadata(defaultValue: false));

    /// <summary>
    /// Absolute on-disk path to the PDF to display. Setting triggers
    /// navigation (immediately if the viewer is ready; queued
    /// otherwise). Setting to <see langword="null"/> or empty
    /// navigates to a blank state.
    /// </summary>
    public string? DocumentPath
    {
        get => (string?)GetValue(DocumentPathProperty);
        set => SetValue(DocumentPathProperty, value);
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
    /// Raised when navigation to a PDF fails — file missing, viewer
    /// assets missing, WebView2 init failed, or WebView2's navigation
    /// pipeline reported a non-success result. Consumers typically
    /// surface the <see cref="PdfViewerNavigationFailedEventArgs.Reason"/>
    /// in a UI banner.
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
            IsViewerReady = true;

            // If a DocumentPath was set before Loaded fired, navigate
            // to it now. Re-checking the current value handles the
            // "set on construction, Loaded fires, navigate" pattern.
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

    private void NavigateToDocument(string pdfPath)
    {
        if (!File.Exists(pdfPath))
        {
            RaiseNavigationFailed(pdfPath, "FileNotFound", exception: null);
            return;
        }

        var viewerHtmlPath = Path.Combine(AppContext.BaseDirectory, ViewerRelativePath);
        if (!File.Exists(viewerHtmlPath))
        {
            RaiseNavigationFailed(pdfPath, "ViewerAssetsMissing", exception: null);
            return;
        }

        var url = BuildViewerUrl(viewerHtmlPath, pdfPath);
        WebView.CoreWebView2!.Navigate(url);
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {
            RaiseNavigationFailed(DocumentPath, "NavigationCompletedWithError", exception: null);
        }
    }

    private void RaiseNavigationFailed(string? attemptedPath, string reason, Exception? exception)
    {
        NavigationFailed?.Invoke(
            this,
            new PdfViewerNavigationFailedEventArgs(attemptedPath, reason, exception));
    }

    /// <summary>
    /// Constructs the PDF.js viewer URL pointing at
    /// <paramref name="viewerHtmlPath"/> with the supplied
    /// <paramref name="pdfPath"/> as its <c>file</c> query
    /// parameter, both URI-encoded so spaces and special characters
    /// in the paths survive the round-trip into the viewer's
    /// JavaScript. Internal so the test surface can pin the URL
    /// shape against accidental drift.
    /// </summary>
    internal static string BuildViewerUrl(string viewerHtmlPath, string pdfPath)
    {
        var pdfFileUri = new Uri(pdfPath).AbsoluteUri;
        var encodedPdf = Uri.EscapeDataString(pdfFileUri);
        var viewerUri = new Uri(viewerHtmlPath).AbsoluteUri;
        return $"{viewerUri}?file={encodedPdf}";
    }
}
