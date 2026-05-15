using AwesomeAssertions;

using EasySynQ.Domain;
using EasySynQ.Domain.Entities.Identity;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace EasySynQ.Tests.Integration.Data;

/// <summary>
/// Verifies the <c>AddPermissionsAndLinkTables</c> migration seeds the
/// Phase 1 system permissions exactly as enumerated in
/// <see cref="PermissionNames"/>. Drift between the migration's seed
/// block and the code-side constants would mean either a permission
/// row that no code references (dead data) or a code path checking a
/// permission name that does not exist (a bug). This test pins the
/// invariant ADR 0007 calls out: the migration is the catalog's
/// source of truth, and the constants in <see cref="PermissionNames"/>
/// must mirror it exactly.
/// </summary>
/// <remarks>
/// Assertions are scoped to <c>Category == "System"</c> so subsequent
/// phases that seed their own permissions (Phase 2 Document Controller
/// adds Document / ExternalDocument / DocumentLink categories per ADR
/// 0008) do not break these Phase-1-specific invariants. Phase 2's
/// own catalog has a parallel test class.
/// </remarks>
public class PermissionsMigrationSeedTests : IntegrationTestBase
{
    private const string SystemCategory = "System";

    [Fact]
    public async Task SeededRowCount_MatchesPermissionNamesAllAsync()
    {
        await using var ctx = NewContext();
        var rows = await ctx.Permissions
            .IgnoreQueryFilters()
            .CountAsync(p => p.Category == SystemCategory, Ct);
        rows.Should().Be(PermissionNames.All.Count);
    }

    [Fact]
    public async Task SeededNames_MatchPermissionNamesAllExactlyAsync()
    {
        await using var ctx = NewContext();
        var seededNames = await ctx.Permissions
            .IgnoreQueryFilters()
            .Where(p => p.Category == SystemCategory)
            .Select(p => p.Name)
            .ToListAsync(Ct);

        seededNames.Should().BeEquivalentTo(PermissionNames.All);
    }

    [Fact]
    public async Task SeededSystemCategoryRows_AreAllPhase1NamesAsync()
    {
        // Every row in the System category should be a Phase 1
        // permission name. Subsequent phases seed under their own
        // category labels (Document / ExternalDocument / DocumentLink
        // for Phase 2 per ADR 0008); they must not contaminate this
        // bucket.
        await using var ctx = NewContext();
        var systemNames = await ctx.Permissions
            .IgnoreQueryFilters()
            .Where(p => p.Category == SystemCategory)
            .Select(p => p.Name)
            .ToListAsync(Ct);

        systemNames.Should().OnlyContain(n => PermissionNames.All.Contains(n));
    }

    [Fact]
    public async Task SeededRows_AreNotSoftDeletedAsync()
    {
        await using var ctx = NewContext();
        var deletedCount = await ctx.Permissions
            .IgnoreQueryFilters()
            .CountAsync(p => p.Category == SystemCategory && p.IsDeleted, Ct);
        deletedCount.Should().Be(0);
    }

    [Fact]
    public async Task SeededIds_AreDeterministicAcrossInstallsAsync()
    {
        // Spot-check two specific Id ↔ Name pairings. The migration
        // commits the entire 11-row catalog to fixed Ids (prefix 07-)
        // so audit-log rows referencing PermissionId Guids stay stable
        // across environments. Spot-checking is sufficient — the full
        // mapping is reviewed at migration commit time.
        await using var ctx = NewContext();
        var administer = await ctx.Permissions
            .IgnoreQueryFilters()
            .SingleAsync(p => p.Name == PermissionNames.SystemAdminister, Ct);
        administer.Id.Should().Be(new Guid("07000000-0000-0000-0000-000000000001"));

        var auditLogRead = await ctx.Permissions
            .IgnoreQueryFilters()
            .SingleAsync(p => p.Name == PermissionNames.AuditLogRead, Ct);
        auditLogRead.Id.Should().Be(new Guid("07000000-0000-0000-0000-00000000000b"));
    }
}
