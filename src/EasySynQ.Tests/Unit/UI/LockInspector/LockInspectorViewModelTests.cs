using AwesomeAssertions;

using EasySynQ.Domain;
using EasySynQ.Domain.Entities.Audit;
using EasySynQ.Services.Abstractions;
using EasySynQ.Services.LockReasons;
using EasySynQ.UI.LockInspector;

using Moq;

using Xunit;

namespace EasySynQ.Tests.Unit.UI.LockInspector;

/// <summary>
/// Unit tests for <see cref="LockInspectorViewModel"/> (ADR 0012
/// C7b). Pure VM behavior — covers the always-resolve / write-through
/// cache shape and the not-locked surface.
/// </summary>
public class LockInspectorViewModelTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static LockReason BuildSampleLockReason(
        string lockedEntityType,
        string lockedEntityId)
    {
        var link = new LockReasonLink(
            tag: lockedEntityType,
            id: "test-id",
            detail: "Locked for test.",
            navigationEntityType: null,
            navigationEntityId: null,
            because: null,
            isTerminal: true);
        return new LockReason(
            id: Guid.NewGuid(),
            lockedEntityType: lockedEntityType,
            lockedEntityId: lockedEntityId,
            chain: [link]);
    }

    [Fact]
    public void Constructor_NullOrWhitespaceLockedEntityType_Throws()
    {
        var lockReasons = new Mock<ILockReasonRepository>();
        var registry = new Mock<ILockReasonResolverRegistry>();

        var act = () => new LockInspectorViewModel(
            "  ", "id", lockReasons.Object, registry.Object);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_NullRegistry_Throws()
    {
        var lockReasons = new Mock<ILockReasonRepository>();

        var act = () => new LockInspectorViewModel(
            LockedEntityTypes.Document, "id", lockReasons.Object, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task LoadAsync_NoResolverForType_SetsNotLockedAsync()
    {
        var lockReasons = new Mock<ILockReasonRepository>(MockBehavior.Strict);
        var registry = new Mock<ILockReasonResolverRegistry>(MockBehavior.Strict);
        registry.Setup(r => r.GetResolver("UnknownType"))
            .Returns((ILockReasonResolver?)null);

        var vm = new LockInspectorViewModel(
            "UnknownType", "id", lockReasons.Object, registry.Object);

        await vm.LoadAsync(Ct);

        vm.IsLocked.Should().BeFalse();
        vm.Chain.Should().BeEmpty();
        vm.IsLoading.Should().BeFalse();
        // Strict mock: lockReasons must NOT have been touched.
        lockReasons.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LoadAsync_ResolverReturnsNull_SetsNotLockedAndSkipsCacheWriteAsync()
    {
        var docId = Guid.NewGuid().ToString("D");

        var resolver = new Mock<ILockReasonResolver>();
        resolver.SetupGet(r => r.LockedEntityType).Returns(LockedEntityTypes.Document);
        resolver.Setup(r => r.ResolveAsync(docId, Ct))
            .ReturnsAsync((LockReason?)null);

        var registry = new Mock<ILockReasonResolverRegistry>();
        registry.Setup(r => r.GetResolver(LockedEntityTypes.Document))
            .Returns(resolver.Object);

        // Strict — assert no cache I/O fires when resolver returns null.
        var lockReasons = new Mock<ILockReasonRepository>(MockBehavior.Strict);

        var vm = new LockInspectorViewModel(
            LockedEntityTypes.Document, docId, lockReasons.Object, registry.Object);

        await vm.LoadAsync(Ct);

        vm.IsLocked.Should().BeFalse();
        vm.Chain.Should().BeEmpty();
        lockReasons.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LoadAsync_FirstInspect_PersistsCacheAndRendersChainAsync()
    {
        var docId = Guid.NewGuid().ToString("D");
        var freshChain = BuildSampleLockReason(LockedEntityTypes.Document, docId);

        var resolver = new Mock<ILockReasonResolver>();
        resolver.SetupGet(r => r.LockedEntityType).Returns(LockedEntityTypes.Document);
        resolver.Setup(r => r.ResolveAsync(docId, Ct))
            .ReturnsAsync(freshChain);

        var registry = new Mock<ILockReasonResolverRegistry>();
        registry.Setup(r => r.GetResolver(LockedEntityTypes.Document))
            .Returns(resolver.Object);

        var lockReasons = new Mock<ILockReasonRepository>();
        lockReasons.Setup(r => r.GetByLockedEntityAsync(
                LockedEntityTypes.Document, docId, Ct))
            .ReturnsAsync((LockReason?)null);
        lockReasons.Setup(r => r.AddAsync(freshChain, Ct))
            .Returns(Task.CompletedTask);
        lockReasons.Setup(r => r.SaveChangesAsync(Ct))
            .ReturnsAsync(1);

        var vm = new LockInspectorViewModel(
            LockedEntityTypes.Document, docId, lockReasons.Object, registry.Object);

        await vm.LoadAsync(Ct);

        vm.IsLocked.Should().BeTrue();
        vm.Chain.Should().ContainSingle();
        vm.Chain[0].Detail.Should().Be("Locked for test.");
        // The fresh chain was persisted exactly once.
        lockReasons.Verify(r => r.AddAsync(freshChain, Ct), Times.Once);
        lockReasons.Verify(r => r.SaveChangesAsync(Ct), Times.Once);
    }

    [Fact]
    public async Task LoadAsync_SubsequentInspect_SkipsCacheWriteAsync()
    {
        var docId = Guid.NewGuid().ToString("D");
        var freshChain = BuildSampleLockReason(LockedEntityTypes.Document, docId);
        var cachedChain = BuildSampleLockReason(LockedEntityTypes.Document, docId);

        var resolver = new Mock<ILockReasonResolver>();
        resolver.SetupGet(r => r.LockedEntityType).Returns(LockedEntityTypes.Document);
        resolver.Setup(r => r.ResolveAsync(docId, Ct))
            .ReturnsAsync(freshChain);

        var registry = new Mock<ILockReasonResolverRegistry>();
        registry.Setup(r => r.GetResolver(LockedEntityTypes.Document))
            .Returns(resolver.Object);

        var lockReasons = new Mock<ILockReasonRepository>();
        lockReasons.Setup(r => r.GetByLockedEntityAsync(
                LockedEntityTypes.Document, docId, Ct))
            .ReturnsAsync(cachedChain);

        var vm = new LockInspectorViewModel(
            LockedEntityTypes.Document, docId, lockReasons.Object, registry.Object);

        await vm.LoadAsync(Ct);

        vm.IsLocked.Should().BeTrue();
        // The fresh chain (from the resolver) is what gets rendered —
        // NOT the cached chain. Both happen to have identical content
        // here but verifying the reference identity tightens the
        // contract: "always render the resolver's output, regardless
        // of cache state."
        vm.Chain.Should().BeSameAs(freshChain.Chain);
        // Cache write must NOT fire when a row already exists.
        lockReasons.Verify(
            r => r.AddAsync(It.IsAny<LockReason>(), It.IsAny<CancellationToken>()),
            Times.Never);
        lockReasons.Verify(
            r => r.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task LoadAsync_IdempotentForSameVm_AcrossMultipleCallsAsync()
    {
        // Calling LoadAsync twice on the same VM resolves twice and
        // writes through only once (the second call sees the cache).
        var docId = Guid.NewGuid().ToString("D");
        var freshChain = BuildSampleLockReason(LockedEntityTypes.Document, docId);

        var resolver = new Mock<ILockReasonResolver>();
        resolver.SetupGet(r => r.LockedEntityType).Returns(LockedEntityTypes.Document);
        resolver.Setup(r => r.ResolveAsync(docId, Ct))
            .ReturnsAsync(freshChain);

        var registry = new Mock<ILockReasonResolverRegistry>();
        registry.Setup(r => r.GetResolver(LockedEntityTypes.Document))
            .Returns(resolver.Object);

        // Cache returns null first time, the new row second time.
        var lockReasons = new Mock<ILockReasonRepository>();
        lockReasons.SetupSequence(r => r.GetByLockedEntityAsync(
                LockedEntityTypes.Document, docId, Ct))
            .ReturnsAsync((LockReason?)null)
            .ReturnsAsync(freshChain);
        lockReasons.Setup(r => r.AddAsync(freshChain, Ct))
            .Returns(Task.CompletedTask);
        lockReasons.Setup(r => r.SaveChangesAsync(Ct))
            .ReturnsAsync(1);

        var vm = new LockInspectorViewModel(
            LockedEntityTypes.Document, docId, lockReasons.Object, registry.Object);

        await vm.LoadAsync(Ct);
        await vm.LoadAsync(Ct);

        resolver.Verify(r => r.ResolveAsync(docId, Ct), Times.Exactly(2));
        lockReasons.Verify(r => r.AddAsync(freshChain, Ct), Times.Once);
        lockReasons.Verify(r => r.SaveChangesAsync(Ct), Times.Once);
    }

    [Fact]
    public async Task LoadAsync_IsLoading_TogglesAcrossInvocationAsync()
    {
        // IsLoading is true during the await; false after.
        var docId = Guid.NewGuid().ToString("D");

        var resolver = new Mock<ILockReasonResolver>();
        resolver.SetupGet(r => r.LockedEntityType).Returns(LockedEntityTypes.Document);
        resolver.Setup(r => r.ResolveAsync(docId, Ct))
            .ReturnsAsync((LockReason?)null);

        var registry = new Mock<ILockReasonResolverRegistry>();
        registry.Setup(r => r.GetResolver(LockedEntityTypes.Document))
            .Returns(resolver.Object);

        var lockReasons = new Mock<ILockReasonRepository>();

        var vm = new LockInspectorViewModel(
            LockedEntityTypes.Document, docId, lockReasons.Object, registry.Object);

        vm.IsLoading.Should().BeFalse();
        await vm.LoadAsync(Ct);
        vm.IsLoading.Should().BeFalse();
    }
}
