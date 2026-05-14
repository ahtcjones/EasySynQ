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
public class PermissionsMigrationSeedTests : IntegrationTestBase
{
    [Fact]
    public async Task SeededRowCount_MatchesPermissionNamesAllAsync()
    {
        await using var ctx = NewContext();
        var rows = await ctx.Permissions.IgnoreQueryFilters().CountAsync(Ct);
        rows.Should().Be(PermissionNames.All.Count);
    }

    [Fact]
    public async Task SeededNames_MatchPermissionNamesAllExactlyAsync()
    {
        await using var ctx = NewContext();
        var seededNames = await ctx.Permissions
            .IgnoreQueryFilters()
            .Select(p => p.Name)
            .ToListAsync(Ct);

        seededNames.Should().BeEquivalentTo(PermissionNames.All);
    }

    [Fact]
    public async Task SeededRows_AllBelongToSystemCategoryAsync()
    {
        // Phase 1 permissions are all IT-side (ADR 0007 — Administrator
        // is reserved for system administration). Operational categories
        // like "Document", "Production" arrive with their phase's
        // migration.
        await using var ctx = NewContext();
        var categories = await ctx.Permissions
            .IgnoreQueryFilters()
            .Select(p => p.Category)
            .Distinct()
            .ToListAsync(Ct);

        categories.Should().ContainSingle().Which.Should().Be("System");
    }

    [Fact]
    public async Task SeededRows_AreNotSoftDeletedAsync()
    {
        await using var ctx = NewContext();
        var deletedCount = await ctx.Permissions
            .IgnoreQueryFilters()
            .CountAsync(p => p.IsDeleted, Ct);
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
