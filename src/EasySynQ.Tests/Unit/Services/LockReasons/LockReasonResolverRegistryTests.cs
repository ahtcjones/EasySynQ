using AwesomeAssertions;

using EasySynQ.Domain;
using EasySynQ.Domain.Entities.Audit;
using EasySynQ.Services.LockReasons;

using Xunit;

namespace EasySynQ.Tests.Unit.Services.LockReasons;

/// <summary>
/// Unit tests for <see cref="LockReasonResolverRegistry"/> (ADR 0012
/// C7a). Verifies the registry's type-keyed dispatch contract and the
/// duplicate-key guard that catches DI misconfiguration loudly rather
/// than silently picking a winner.
/// </summary>
public class LockReasonResolverRegistryTests
{
    private sealed class StubResolver : ILockReasonResolver
    {
        public string LockedEntityType { get; }

        public StubResolver(string lockedEntityType)
        {
            LockedEntityType = lockedEntityType;
        }

        public Task<LockReason?> ResolveAsync(
            string lockedEntityId,
            CancellationToken cancellationToken)
            => Task.FromResult<LockReason?>(null);
    }

    [Fact]
    public void GetResolver_KnownType_ReturnsRegisteredResolver()
    {
        var docResolver = new StubResolver(LockedEntityTypes.Document);
        var revResolver = new StubResolver(LockedEntityTypes.DocumentRevision);
        var registry = new LockReasonResolverRegistry([docResolver, revResolver]);

        registry.GetResolver(LockedEntityTypes.Document).Should().BeSameAs(docResolver);
        registry.GetResolver(LockedEntityTypes.DocumentRevision).Should().BeSameAs(revResolver);
    }

    [Fact]
    public void GetResolver_UnknownType_ReturnsNull()
    {
        var registry = new LockReasonResolverRegistry(
            [new StubResolver(LockedEntityTypes.Document)]);

        registry.GetResolver("Asset").Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetResolver_NullOrWhitespace_ReturnsNull(string? type)
    {
        var registry = new LockReasonResolverRegistry(
            [new StubResolver(LockedEntityTypes.Document)]);

        registry.GetResolver(type!).Should().BeNull();
    }

    [Fact]
    public void Construct_DuplicateLockedEntityType_ThrowsInvalidOperationException()
    {
        var first = new StubResolver(LockedEntityTypes.Document);
        var second = new StubResolver(LockedEntityTypes.Document);

        var act = () => new LockReasonResolverRegistry([first, second]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Duplicate ILockReasonResolver registration*Document*");
    }

    [Fact]
    public void Construct_EmptyResolverSet_AcceptsAndReturnsNullForAnyType()
    {
        var registry = new LockReasonResolverRegistry([]);

        registry.GetResolver(LockedEntityTypes.Document).Should().BeNull();
        registry.GetResolver(LockedEntityTypes.DocumentRevision).Should().BeNull();
    }

    [Fact]
    public void Construct_NullResolverSet_ThrowsArgumentNullException()
    {
        var act = () => new LockReasonResolverRegistry(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
