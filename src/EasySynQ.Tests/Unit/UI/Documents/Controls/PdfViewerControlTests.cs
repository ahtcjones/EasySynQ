using AwesomeAssertions;

using EasySynQ.UI.Documents.Controls;

using Xunit;

namespace EasySynQ.Tests.Unit.UI.Documents.Controls;

/// <summary>
/// Unit tests for <see cref="PdfViewerControl"/> (ADR 0010 C5,
/// amended 2026-05-16 for virtual host mapping). Pinned to the
/// dependency-property surface, event-args shape, and the static URL
/// helpers; the actual WebView2 instance is not constructed (would
/// require a Win32 host window — see ADR 0010 §"For testing"). Real
/// viewer behavior is verified in C6's smoke when the document
/// detail view hosts the control.
/// </summary>
public class PdfViewerControlTests
{
    [Fact]
    public void NavigationFailedEventArgs_RoundtripsAttemptedPathReasonAndException()
    {
        var ex = new InvalidOperationException("boom");
        var args = new PdfViewerNavigationFailedEventArgs(
            attemptedPath: @"C:\fake\path.pdf",
            reason: "FileNotFound",
            exception: ex);

        args.AttemptedPath.Should().Be(@"C:\fake\path.pdf");
        args.Reason.Should().Be("FileNotFound");
        args.Exception.Should().BeSameAs(ex);
    }

    [Fact]
    public void NavigationFailedEventArgs_AcceptsNullAttemptedPathAndException()
    {
        // Both are explicitly nullable on the event args — null
        // attempted-path is the WebView2-init-failure case (no path
        // was being navigated when the failure occurred), null
        // exception is the "navigation reported error but no
        // exception object was raised" case.
        var args = new PdfViewerNavigationFailedEventArgs(
            attemptedPath: null,
            reason: "WebView2InitFailed",
            exception: null);

        args.AttemptedPath.Should().BeNull();
        args.Reason.Should().Be("WebView2InitFailed");
        args.Exception.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NavigationFailedEventArgs_RejectsNullOrWhitespaceReason(string? reason)
    {
        Action act = () => new PdfViewerNavigationFailedEventArgs(
            attemptedPath: @"C:\path.pdf",
            reason: reason!,
            exception: null);

        act.Should().Throw<ArgumentException>();
    }

    // ─── BuildViewerUrl ────────────────────────────────────────────

    [Fact]
    public void BuildViewerUrl_WrapsContentUrlInViewerHostQueryParameter()
    {
        // Post-amendment shape: the viewer host is a constant
        // (easysynq-pdfviewer.local) and the viewer asset path
        // includes the /web/ subdirectory because the viewer-host
        // mapping scopes at PDF.js's distribution PARENT (not at
        // web/) so viewer.mjs's sibling-path imports of ../build/
        // resolve correctly. See ADR 0010 amendment §"Viewer-host
        // scope refinement (smoke walk #3)".
        var contentUrl = "https://easysynq-pdfcontent.local/2a/abcdef.pdf";

        var url = PdfViewerControl.BuildViewerUrl(contentUrl);

        url.Should().StartWith("https://easysynq-pdfviewer.local/web/viewer.html?file=");
        // The content URL is URL-encoded so it survives as a single
        // query-string value (the path's slashes become %2F, the
        // colon becomes %3A, etc.).
        url.Should().Contain("https%3A%2F%2Feasysynq-pdfcontent.local%2F2a%2Fabcdef.pdf");
    }

    [Fact]
    public void BuildViewerUrl_HandlesContentUrlWithEncodedCharacters()
    {
        // Round-trip safety: even a URL that already contains %20
        // (encoded space) etc. should survive intact under
        // EscapeDataString.
        var contentUrl = "https://easysynq-pdfcontent.local/some%20folder/file.pdf";

        var url = PdfViewerControl.BuildViewerUrl(contentUrl);

        // %20 in input → %2520 in output (the % itself becomes %25).
        url.Should().Contain("some%2520folder");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void BuildViewerUrl_RejectsNullOrEmptyContentUrl(string? contentUrl)
    {
        Action act = () => PdfViewerControl.BuildViewerUrl(contentUrl!);
        act.Should().Throw<ArgumentException>();
    }

    // ─── BuildContentUrl ───────────────────────────────────────────

    [Fact]
    public void BuildContentUrl_TranslatesAbsolutePathToVirtualHostUrl()
    {
        var contentRoot = @"C:\Users\Dev-CoJo\AppData\Local\EasySynQ\vault";
        var filePath = @"C:\Users\Dev-CoJo\AppData\Local\EasySynQ\vault\2a\abcdef.pdf";

        var url = PdfViewerControl.BuildContentUrl(filePath, contentRoot);

        url.Should().Be("https://easysynq-pdfcontent.local/2a/abcdef.pdf");
    }

    [Fact]
    public void BuildContentUrl_ConvertsBackslashesToForwardSlashes()
    {
        // Windows file paths use '\'; the URL must use '/' regardless
        // of host OS.
        var contentRoot = @"C:\vault";
        var filePath = @"C:\vault\ab\cd\file.pdf";

        var url = PdfViewerControl.BuildContentUrl(filePath, contentRoot);

        url.Should().Be("https://easysynq-pdfcontent.local/ab/cd/file.pdf");
    }

    [Fact]
    public void BuildContentUrl_FlatFileUnderRoot_NoSubdirectory()
    {
        var contentRoot = @"C:\vault";
        var filePath = @"C:\vault\file.pdf";

        var url = PdfViewerControl.BuildContentUrl(filePath, contentRoot);

        url.Should().Be("https://easysynq-pdfcontent.local/file.pdf");
    }

    [Theory]
    [InlineData(null, @"C:\vault")]
    [InlineData("", @"C:\vault")]
    [InlineData(@"C:\vault\file.pdf", null)]
    [InlineData(@"C:\vault\file.pdf", "")]
    public void BuildContentUrl_RejectsNullOrEmptyArgs(string? filePath, string? contentRoot)
    {
        Action act = () => PdfViewerControl.BuildContentUrl(filePath!, contentRoot!);
        act.Should().Throw<ArgumentException>();
    }
}
