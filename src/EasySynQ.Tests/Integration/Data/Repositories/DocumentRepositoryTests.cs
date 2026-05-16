using AwesomeAssertions;

using EasySynQ.Data.Context;
using EasySynQ.Domain.Entities.Documents;
using EasySynQ.Services.Abstractions;
using EasySynQ.Tests.Integration.Services;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace EasySynQ.Tests.Integration.Data.Repositories;

/// <summary>
/// Integration tests for
/// <see cref="IDocumentRepository.GetNonRetiredAsync"/> (ADR 0008 C6a).
/// Backs the Document list view's default population. Inherits from
/// <see cref="ServiceIntegrationTestBase"/> for the full DI graph.
/// </summary>
public class DocumentRepositoryTests : ServiceIntegrationTestBase
{
    private async Task PersistDocumentsAsync(EasySynQDbContext ctx, params Document[] documents)
    {
        CurrentUser.UserId = Guid.NewGuid();
        CurrentUser.Username = "tester";
        CurrentUser.Roles = ["TestRole"];

        foreach (var d in documents)
        {
            ctx.Documents.Add(d);
        }
        await ctx.SaveChangesAsync(Ct);
    }

    private static async Task<IReadOnlyList<Document>> ListAsync(AsyncServiceScope scope)
    {
        var repo = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
        return await repo.GetNonRetiredAsync(Ct);
    }

    [Fact]
    public async Task GetNonRetired_EmptyDb_ReturnsEmptyAsync()
    {
        await using var scope = NewScope();
        var result = await ListAsync(scope);
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetNonRetired_ReturnsAllNonRetiredOrderedByNumberAsync()
    {
        var docC = new Document(Guid.NewGuid(), "SOP-C-001", "Charlie");
        var docA = new Document(Guid.NewGuid(), "SOP-A-001", "Alpha");
        var docB = new Document(Guid.NewGuid(), "SOP-B-001", "Bravo");

        // Insert out-of-order to prove ordering is by Number, not by
        // insertion order.
        await using (var ctx = NewContext())
        {
            await PersistDocumentsAsync(ctx, docC, docA, docB);
        }

        await using var scope = NewScope();
        var result = await ListAsync(scope);

        result.Should().HaveCount(3);
        result.Select(d => d.Number).Should().Equal("SOP-A-001", "SOP-B-001", "SOP-C-001");
    }

    [Fact]
    public async Task GetNonRetired_ExcludesRetiredDocumentsAsync()
    {
        var sigId = Guid.NewGuid();
        var retirerId = Guid.NewGuid();

        var live = new Document(Guid.NewGuid(), "SOP-LIVE-001", "Live");
        var retired = new Document(Guid.NewGuid(), "SOP-RET-001", "Retired");
        retired.Retire(Clock.UtcNow, retirerId, sigId);

        await using (var ctx = NewContext())
        {
            // Insert the signature row referenced by the retired
            // document so the foreign-key style soft reference (per
            // ADR 0004 there is no enforced FK on Signatures, but we
            // still want a realistic fixture) has a real target.
            ctx.Signatures.Add(new EasySynQ.Domain.Entities.Audit.Signature(
                id: sigId,
                utcTimestamp: Clock.UtcNow,
                roleAtTimeOfSign: "TestRole",
                signedEntityType: nameof(Document),
                signedEntityId: retired.Id.ToString(),
                payloadHash: new string('a', 64)));
            await PersistDocumentsAsync(ctx, live, retired);
        }

        await using var scope = NewScope();
        var result = await ListAsync(scope);

        result.Should().ContainSingle().Which.Id.Should().Be(live.Id);
    }

    [Fact]
    public async Task GetNonRetired_ExcludesSoftDeletedDocumentsAsync()
    {
        var live = new Document(Guid.NewGuid(), "SOP-LIVE-002", "Live");
        var toSoftDelete = new Document(Guid.NewGuid(), "SOP-SD-001", "Soft Deleted");

        await using (var ctx = NewContext())
        {
            await PersistDocumentsAsync(ctx, live, toSoftDelete);
        }

        // Soft-delete one of them.
        await using (var scope = NewScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
            var target = await repo.GetByIdAsync(toSoftDelete.Id, Ct);
            target.Should().NotBeNull();
            repo.SoftDelete(target!);
            await repo.SaveChangesAsync(Ct);
        }

        await using var scope2 = NewScope();
        var result = await ListAsync(scope2);

        result.Should().ContainSingle().Which.Id.Should().Be(live.Id);
    }
}
