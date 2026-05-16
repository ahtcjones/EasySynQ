using AwesomeAssertions;

using EasySynQ.UI.Documents.Controls;

using Xunit;

namespace EasySynQ.Tests.Unit.UI.Documents.Controls;

/// <summary>
/// Unit tests for <see cref="PdfViewerControl"/> (ADR 0010 C5).
/// Pinned to the dependency-property surface and event-args shape;
/// the actual WebView2 instance is not constructed (would require
/// a Win32 host window — see ADR 0010 §"For testing"). Real viewer
/// behavior is verified in C6's smoke when the document detail view
/// hosts the control.
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

    [Fact]
    public void BuildViewerUrl_EncodesPdfPathAsFileUriQueryParameter()
    {
        // Internal helper exposed via InternalsVisibleTo; pin the URL
        // shape so accidental drift in the encoding logic surfaces in
        // tests rather than at runtime against the bundled viewer.
        var viewer = @"C:\App\Assets\pdfviewer\web\viewer.html";
        var pdf = @"C:\App\vault\ab\abcdef.pdf";

        var url = PdfViewerControl.BuildViewerUrl(viewer, pdf);

        url.Should().StartWith("file:///C:/App/Assets/pdfviewer/web/viewer.html?file=");
        // The PDF file URI is itself URI-encoded so it survives as
        // a single query-string value (the path's slashes become
        // %2F, the colon becomes %3A, etc.).
        url.Should().Contain("file%3A%2F%2F%2FC%3A%2FApp%2Fvault%2Fab%2Fabcdef.pdf");
    }

    [Fact]
    public void BuildViewerUrl_HandlesSpacesInPath()
    {
        var viewer = @"C:\App With Space\viewer.html";
        var pdf = @"C:\vault\ab\abcdef.pdf";

        var url = PdfViewerControl.BuildViewerUrl(viewer, pdf);

        // Spaces in the viewer path become %20 in the file:// URI.
        url.Should().Contain("App%20With%20Space");
        url.Should().Contain("?file=");
    }
}
