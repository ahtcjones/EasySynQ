using AwesomeAssertions;

using EasySynQ.Domain.ValueObjects;

using Xunit;

namespace EasySynQ.Tests.Unit.Domain;

public class DocumentRevisionRefTests
{
    private static readonly Guid DocId =
        new("11111111-1111-1111-1111-111111111111");

    private static readonly DateTime EffectiveFrom =
        new(2026, 2, 14, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Equality_SameValuesAreEqual()
    {
        var a = new DocumentRevisionRef(DocId, "Rev B", EffectiveFrom);
        var b = new DocumentRevisionRef(DocId, "Rev B", EffectiveFrom);

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Equality_DifferentDocumentIdsAreNotEqual()
    {
        var a = new DocumentRevisionRef(DocId, "Rev B", EffectiveFrom);
        var b = new DocumentRevisionRef(Guid.NewGuid(), "Rev B", EffectiveFrom);

        a.Should().NotBe(b);
    }

    [Fact]
    public void Equality_DifferentRevisionTagsAreNotEqual()
    {
        var a = new DocumentRevisionRef(DocId, "Rev B", EffectiveFrom);
        var b = new DocumentRevisionRef(DocId, "Rev C", EffectiveFrom);

        a.Should().NotBe(b);
    }

    [Fact]
    public void Equality_DifferentEffectiveFromAreNotEqual()
    {
        var a = new DocumentRevisionRef(DocId, "Rev B", EffectiveFrom);
        var b = new DocumentRevisionRef(DocId, "Rev B", EffectiveFrom.AddDays(1));

        a.Should().NotBe(b);
    }

    [Fact]
    public void SnapshotSemantics_FieldsAreCapturedAsProvided()
    {
        var refA = new DocumentRevisionRef(DocId, "Rev B", EffectiveFrom);

        refA.DocumentId.Should().Be(DocId);
        refA.RevisionTag.Should().Be("Rev B");
        refA.EffectiveFromUtc.Should().Be(EffectiveFrom);
    }

    [Fact]
    public void Constructor_RejectsEmptyDocumentId()
    {
        Action act = () => new DocumentRevisionRef(Guid.Empty, "Rev B", EffectiveFrom);
        act.Should().Throw<ArgumentException>().WithMessage("*Guid.Empty*");
    }

    [Fact]
    public void Constructor_RejectsEmptyRevisionTag()
    {
        Action act = () => new DocumentRevisionRef(DocId, string.Empty, EffectiveFrom);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_RejectsNonUtcEffectiveFrom()
    {
        var local = new DateTime(2026, 2, 14, 0, 0, 0, DateTimeKind.Local);
        Action act = () => new DocumentRevisionRef(DocId, "Rev B", local);
        act.Should().Throw<ArgumentException>().WithMessage("*DateTimeKind.Utc*");
    }
}
