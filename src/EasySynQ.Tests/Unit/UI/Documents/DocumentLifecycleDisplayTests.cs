using AwesomeAssertions;

using EasySynQ.Domain.Enums;
using EasySynQ.UI.Documents;

using Xunit;

namespace EasySynQ.Tests.Unit.UI.Documents;

/// <summary>
/// Unit tests for <see cref="DocumentLifecycleDisplay.Format"/>.
/// Pins the human-readable rendering across the lifecycle enum,
/// including the Approved-vs-Active gap-window rule per ADR 0008's
/// "stored state is the source of truth" lesson.
/// </summary>
public class DocumentLifecycleDisplayTests
{
    private static readonly DateTime AsOf =
        new(2026, 5, 16, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Draft_Renders_Draft()
        => DocumentLifecycleDisplay.Format(DocumentLifecycle.Draft, null, AsOf)
            .Should().Be("Draft");

    [Fact]
    public void InReview_Renders_InReview()
        => DocumentLifecycleDisplay.Format(DocumentLifecycle.InReview, null, AsOf)
            .Should().Be("In Review");

    [Fact]
    public void Approved_NullEffectiveFromUtc_Renders_Active()
        => DocumentLifecycleDisplay.Format(DocumentLifecycle.Approved, null, AsOf)
            .Should().Be("Active");

    [Fact]
    public void Approved_EffectiveInPast_Renders_Active()
        => DocumentLifecycleDisplay.Format(
            DocumentLifecycle.Approved, AsOf.AddDays(-30), AsOf)
            .Should().Be("Active");

    [Fact]
    public void Approved_EffectiveExactlyAsOf_Renders_Active()
        => DocumentLifecycleDisplay.Format(
            DocumentLifecycle.Approved, AsOf, AsOf)
            .Should().Be("Active");

    [Fact]
    public void Approved_EffectiveInFuture_RendersGapWindowText()
    {
        var future = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

        var result = DocumentLifecycleDisplay.Format(
            DocumentLifecycle.Approved, future, AsOf);

        result.Should().Be("Approved (effective 2026-09-01)");
    }

    [Fact]
    public void Superseded_Renders_Superseded()
        => DocumentLifecycleDisplay.Format(DocumentLifecycle.Superseded, null, AsOf)
            .Should().Be("Superseded");

    [Fact]
    public void Archived_Renders_Archived()
        => DocumentLifecycleDisplay.Format(DocumentLifecycle.Archived, null, AsOf)
            .Should().Be("Archived");
}
