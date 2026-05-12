using AwesomeAssertions;

using EasySynQ.Domain.Entities.Audit;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace EasySynQ.Tests.Integration.Data;

public class OwnedCollectionTests : IntegrationTestBase
{
    [Fact]
    public async Task LockReasonChain_MultiLink_RoundTripsIncludingNullableFieldsAsync()
    {
        var chain = new[]
        {
            new LockReasonLink(
                tag: "Job", id: "J-2026-0847", detail: "Final Release blocked",
                navigationEntityType: "Job", navigationEntityId: "J-2026-0847",
                because: "because a non-conformance is open against it",
                isTerminal: false),
            new LockReasonLink(
                tag: "NCR", id: "NCR-2026-0033", detail: "Open · Major severity",
                navigationEntityType: "NCR", navigationEntityId: "NCR-2026-0033",
                because: "because a hardness reading fell outside tolerance",
                isTerminal: false),
            new LockReasonLink(
                tag: "Reading", id: "R-4", detail: "36.4 HRC · below 38.0 minimum",
                navigationEntityType: null, navigationEntityId: null,
                because: null,
                isTerminal: true),
        };

        var id = Guid.NewGuid();
        var reason = new LockReason(id, "Job", "J-2026-0847", chain);

        await using (var ctx = NewContext())
        {
            ctx.LockReasons.Add(reason);
            await ctx.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            var loaded = await ctx.LockReasons.SingleAsync(lr => lr.Id == id, Ct);

            loaded.Chain.Should().HaveCount(3);

            // First link: full set including nav + because
            loaded.Chain[0].Tag.Should().Be("Job");
            loaded.Chain[0].Id.Should().Be("J-2026-0847");
            loaded.Chain[0].NavigationEntityType.Should().Be("Job");
            loaded.Chain[0].Because.Should().Be("because a non-conformance is open against it");
            loaded.Chain[0].IsTerminal.Should().BeFalse();

            // Middle link
            loaded.Chain[1].Tag.Should().Be("NCR");
            loaded.Chain[1].Because.Should().StartWith("because a hardness");

            // Terminal: nav and because are null
            loaded.Chain[2].IsTerminal.Should().BeTrue();
            loaded.Chain[2].NavigationEntityType.Should().BeNull();
            loaded.Chain[2].NavigationEntityId.Should().BeNull();
            loaded.Chain[2].Because.Should().BeNull();
        }
    }
}
