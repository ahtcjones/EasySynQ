using AwesomeAssertions;

using EasySynQ.Domain.Entities.Documents;

using Xunit;

namespace EasySynQ.Tests.Unit.Domain.Entities.Documents;

/// <summary>
/// Unit tests for <see cref="Document.Retire"/> (ADR 0008 C3) — pure
/// state-machine guards on the entity, no DI graph.
/// </summary>
public class DocumentRetireTests
{
    private static Document NewDocument() =>
        new(Guid.NewGuid(), "SOP-Q-001", "Title");

    private static DateTime UtcNow() => new(2026, 5, 15, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Retire_OnFreshDocument_PopulatesAllThreeFields()
    {
        var doc = NewDocument();
        var retiredAt = UtcNow();
        var actor = Guid.NewGuid();
        var sigId = Guid.NewGuid();

        doc.Retire(retiredAt, actor, sigId);

        doc.RetiredAtUtc.Should().Be(retiredAt);
        doc.RetiredByUserId.Should().Be(actor);
        doc.RetirementSignatureId.Should().Be(sigId);
    }

    [Fact]
    public void Retire_OnAlreadyRetiredDocument_Throws()
    {
        var doc = NewDocument();
        doc.Retire(UtcNow(), Guid.NewGuid(), Guid.NewGuid());

        Action act = () => doc.Retire(UtcNow().AddMinutes(5), Guid.NewGuid(), Guid.NewGuid());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*already been retired*");
    }

    [Fact]
    public void Retire_RejectsNonUtcRetiredAt()
    {
        var doc = NewDocument();
        var localTime = new DateTime(2026, 5, 15, 12, 0, 0, DateTimeKind.Local);

        Action act = () => doc.Retire(localTime, Guid.NewGuid(), Guid.NewGuid());

        act.Should().Throw<ArgumentException>()
            .WithMessage("*RetiredAtUtc must have DateTimeKind.Utc*");
    }

    [Fact]
    public void Retire_RejectsEmptyRetiredByUserId()
    {
        var doc = NewDocument();

        Action act = () => doc.Retire(UtcNow(), Guid.Empty, Guid.NewGuid());

        act.Should().Throw<ArgumentException>()
            .WithMessage("*RetiredByUserId must not be Guid.Empty*");
    }

    [Fact]
    public void Retire_RejectsEmptyRetirementSignatureId()
    {
        var doc = NewDocument();

        Action act = () => doc.Retire(UtcNow(), Guid.NewGuid(), Guid.Empty);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*RetirementSignatureId must not be Guid.Empty*");
    }
}
