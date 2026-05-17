using AwesomeAssertions;

using EasySynQ.Data.Repositories;
using EasySynQ.Domain;
using EasySynQ.Domain.Entities.Audit;
using EasySynQ.Tests.Integration.Data.Interceptors;

using Xunit;

namespace EasySynQ.Tests.Integration.Data.Repositories;

/// <summary>
/// Integration tests for <see cref="LockReasonRepository"/> (ADR 0012
/// C7a). Verifies the JSON-in-column chain round-trip via
/// <c>OwnsMany(...).ToJson()</c> and the indexed
/// <c>(LockedEntityType, LockedEntityId)</c> lookup that backs the
/// inspector cache-first read path.
/// </summary>
public class LockReasonRepositoryTests : InterceptorIntegrationTestBase
{
    private static LockReasonLink TerminalLink(string tag, string id, string detail)
        => new(tag: tag, id: id, detail: detail,
            navigationEntityType: null, navigationEntityId: null,
            because: null, isTerminal: true);

    private static LockReasonLink NonTerminalLink(
        string tag, string id, string detail, string because,
        string? navType = null, string? navId = null)
        => new(tag: tag, id: id, detail: detail,
            navigationEntityType: navType, navigationEntityId: navId,
            because: because, isTerminal: false);

    [Fact]
    public async Task GetByLockedEntityAsync_RoundTripsSingleLinkChainAsync()
    {
        var entityId = Guid.NewGuid().ToString("D");
        var lockReason = new LockReason(
            id: Guid.NewGuid(),
            lockedEntityType: LockedEntityTypes.Document,
            lockedEntityId: entityId,
            chain: [TerminalLink(LockedEntityTypes.Document, "SOP-001", "Retired.")]);

        await using (var ctx = NewContext())
        {
            ctx.LockReasons.Add(lockReason);
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            var repo = new LockReasonRepository(ctx);
            var result = await repo.GetByLockedEntityAsync(
                LockedEntityTypes.Document, entityId, Ct);

            result.Should().NotBeNull();
            result!.Chain.Should().ContainSingle();
            result.Chain[0].Tag.Should().Be(LockedEntityTypes.Document);
            result.Chain[0].Id.Should().Be("SOP-001");
            result.Chain[0].Detail.Should().Be("Retired.");
            result.Chain[0].IsTerminal.Should().BeTrue();
        }
    }

    [Fact]
    public async Task GetByLockedEntityAsync_RoundTripsMultiLinkChainAsync()
    {
        var entityId = Guid.NewGuid().ToString("D");
        var successorId = Guid.NewGuid().ToString("D");
        var lockReason = new LockReason(
            id: Guid.NewGuid(),
            lockedEntityType: LockedEntityTypes.DocumentRevision,
            lockedEntityId: entityId,
            chain:
            [
                NonTerminalLink(
                    LockedEntityTypes.DocumentRevision, "Rev A",
                    "Superseded by Rev B.", "superseded by",
                    navType: LockedEntityTypes.DocumentRevision, navId: successorId),
                TerminalLink(
                    LockedEntityTypes.DocumentRevision, "Rev B",
                    "Approved on 2026-05-17."),
            ]);

        await using (var ctx = NewContext())
        {
            ctx.LockReasons.Add(lockReason);
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            var repo = new LockReasonRepository(ctx);
            var result = await repo.GetByLockedEntityAsync(
                LockedEntityTypes.DocumentRevision, entityId, Ct);

            result.Should().NotBeNull();
            result!.Chain.Should().HaveCount(2);
            result.Chain[0].Because.Should().Be("superseded by");
            result.Chain[0].NavigationEntityType.Should().Be(LockedEntityTypes.DocumentRevision);
            result.Chain[0].NavigationEntityId.Should().Be(successorId);
            result.Chain[0].IsTerminal.Should().BeFalse();
            result.Chain[1].Id.Should().Be("Rev B");
            result.Chain[1].IsTerminal.Should().BeTrue();
        }
    }

    [Fact]
    public async Task GetByLockedEntityAsync_NoMatchingRow_ReturnsNullAsync()
    {
        await using var ctx = NewContext();
        var repo = new LockReasonRepository(ctx);

        var result = await repo.GetByLockedEntityAsync(
            LockedEntityTypes.Document,
            Guid.NewGuid().ToString("D"),
            Ct);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByLockedEntityAsync_DistinguishesByTypeAndIdPairAsync()
    {
        // Same id under two types must not collide.
        var sharedId = Guid.NewGuid().ToString("D");

        await using (var ctx = NewContext())
        {
            ctx.LockReasons.Add(new LockReason(
                id: Guid.NewGuid(),
                lockedEntityType: LockedEntityTypes.Document,
                lockedEntityId: sharedId,
                chain: [TerminalLink(LockedEntityTypes.Document, "SOP-A", "Doc retired.")]));
            ctx.LockReasons.Add(new LockReason(
                id: Guid.NewGuid(),
                lockedEntityType: LockedEntityTypes.DocumentRevision,
                lockedEntityId: sharedId,
                chain: [TerminalLink(LockedEntityTypes.DocumentRevision, "Rev A", "Rev archived.")]));
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            var repo = new LockReasonRepository(ctx);
            var doc = await repo.GetByLockedEntityAsync(
                LockedEntityTypes.Document, sharedId, Ct);
            var rev = await repo.GetByLockedEntityAsync(
                LockedEntityTypes.DocumentRevision, sharedId, Ct);

            doc.Should().NotBeNull();
            doc!.Chain[0].Detail.Should().Be("Doc retired.");
            rev.Should().NotBeNull();
            rev!.Chain[0].Detail.Should().Be("Rev archived.");
        }
    }

    [Fact]
    public async Task GetByLockedEntityAsync_SoftDeletedRow_FilteredOutAsync()
    {
        var entityId = Guid.NewGuid().ToString("D");

        await using (var ctx = NewContext())
        {
            var lockReason = new LockReason(
                id: Guid.NewGuid(),
                lockedEntityType: LockedEntityTypes.Document,
                lockedEntityId: entityId,
                chain: [TerminalLink(LockedEntityTypes.Document, "SOP-001", "Retired.")]);
            ctx.LockReasons.Add(lockReason);
            await ctx.SaveChangesAsync(Ct);

            // Soft-delete via the same context's repository surface so
            // the standard-fields + audit interceptors fire.
            var repo = new LockReasonRepository(ctx);
            repo.SoftDelete(lockReason);
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            var repo = new LockReasonRepository(ctx);
            var result = await repo.GetByLockedEntityAsync(
                LockedEntityTypes.Document, entityId, Ct);

            result.Should().BeNull();
        }
    }

    [Fact]
    public async Task GetByLockedEntityAsync_EmptyType_ThrowsAsync()
    {
        await using var ctx = NewContext();
        var repo = new LockReasonRepository(ctx);

        var act = async () => await repo.GetByLockedEntityAsync(
            "", Guid.NewGuid().ToString("D"), Ct);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetByLockedEntityAsync_EmptyId_ThrowsAsync()
    {
        await using var ctx = NewContext();
        var repo = new LockReasonRepository(ctx);

        var act = async () => await repo.GetByLockedEntityAsync(
            LockedEntityTypes.Document, "", Ct);

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
