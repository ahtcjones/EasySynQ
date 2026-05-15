using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

using AwesomeAssertions;

using EasySynQ.UI;

using Microsoft.Extensions.Logging;

using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Extensions.Logging;
using Serilog.Formatting.Display;

using Xunit;

namespace EasySynQ.Tests.Unit.UI;

/// <summary>
/// Pins the structural shape of <see cref="App.FileSinkOutputTemplate"/>
/// — timestamp, level token, source context, and message rendered in
/// that order — so accidental format regressions surface here instead
/// of in production log files. Routes through the production logging
/// pipeline (Microsoft <see cref="ILogger{T}"/> →
/// <see cref="SerilogLoggerProvider"/> → Serilog <see cref="LogEvent"/>
/// → <see cref="MessageTemplateTextFormatter"/>) so the test exercises
/// the same code path as the file sink.
/// </summary>
/// <remarks>
/// <para>
/// <b>Intentionally not asserting EventId rendering.</b> Follow-Up #12
/// (raw-log EventId inline rendering) was closed as not-feasible —
/// see <see cref="App.FileSinkOutputTemplate"/>'s remarks for the
/// grammar limitation. The EventId property remains attached to each
/// LogEvent and is accessible to any structured-log consumer; it is
/// just not part of the text rendering. This test does not pin its
/// absence either — that's an implementation choice, not a contract.
/// </para>
/// </remarks>
public partial class AppSerilogOutputTemplateTests
{
    [Fact]
    public void FileSinkOutputTemplate_RendersExpectedShape()
    {
        var capture = new CaptureSink();

        using (var serilog = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Sink(capture)
            .CreateLogger())
        {
            using var factory = new SerilogLoggerFactory(serilog, dispose: false);
            var logger = factory.CreateLogger("EasySynQ.Tests.Unit.UI.Sample");

            LogTestEmit(logger);
        }

        var formatter = new MessageTemplateTextFormatter(
            App.FileSinkOutputTemplate,
            CultureInfo.InvariantCulture);
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        formatter.Format(capture.Events.Single(), writer);

        // Shape assertion: a timestamp (YYYY-MM-DD HH:MM:SS.fff with
        // timezone offset), a 3-char bracketed level, the source
        // context with a trailing colon, then the message text. The
        // regex is anchored at the start of the rendered string and
        // tolerant of any timezone offset; it is loose enough to
        // survive minor renderer variation (whitespace, exception
        // suffix), strict enough to catch reordering or token removal.
        var rendered = writer.ToString();
        var shape = new Regex(
            @"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+\-]\d{2}:\d{2} \[INF\] EasySynQ\.Tests\.Unit\.UI\.Sample: test message");
        shape.IsMatch(rendered).Should().BeTrue(
            because: $"rendered text was: {rendered}");
    }

    /// <summary>
    /// Source-generated emit matching the production
    /// <c>[LoggerMessage]</c> shape. The test does not assert against
    /// the EventId in the rendered text (see class-level remarks),
    /// but using <c>[LoggerMessage]</c> keeps the pipeline the test
    /// exercises byte-for-byte equivalent to the production emit
    /// path.
    /// </summary>
    [LoggerMessage(
        EventId = 1001,
        EventName = "TestEvent",
        Level = LogLevel.Information,
        Message = "test message")]
    private static partial void LogTestEmit(Microsoft.Extensions.Logging.ILogger logger);

    private sealed class CaptureSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }
}
