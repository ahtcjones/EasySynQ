using AwesomeAssertions;

using EasySynQ.Domain;
using EasySynQ.Domain.Entities.Identity;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace EasySynQ.Tests.Integration.Data;

/// <summary>
/// Verifies the <c>AddDocumentControllerTables</c> migration seeds the
/// Phase 2 document-controller permissions exactly as enumerated in
/// <see cref="PermissionNames.Phase2Document"/> and creates the
/// QualityManager role with the documented default permission set
/// (<see cref="PermissionNames.QualityManagerDefaults"/> — every
/// document permission except <c>Document.AssignReviewers</c>, per
/// ADR 0008).
/// </summary>
/// <remarks>
/// <para>
/// Drift between the migration's seed block and the code-side constants
/// would mean either a permission row that no code references (dead
/// data) or a code path checking a permission name that does not exist
/// (a bug). These tests pin the invariant ADR 0008 calls out at the
/// migration level: the migration is the catalog's source of truth,
/// and the constants must mirror it exactly.
/// </para>
/// <para>
/// The defensive negative assertion on <c>Document.AssignReviewers</c>
/// is the load-bearing test for a deliberate-omission decision —
/// organizations grant <c>Document.AssignReviewers</c> either to
/// author roles (small-shop default) or restrict it to QM
/// (strict-gatekeeper policy), and the seeded role must not pre-empt
/// either choice.
/// </para>
/// </remarks>
public class DocumentControllerMigrationSeedTests : IntegrationTestBase
{
    private const string DocumentCategory = "Document";
    private const string ExternalDocumentCategory = "ExternalDocument";
    private const string DocumentLinkCategory = "DocumentLink";

    private static readonly Guid QualityManagerRoleId =
        new("08100000-0000-0000-0000-000000000001");

    [Fact]
    public async Task SeededPermissionNames_MatchPhase2DocumentListExactlyAsync()
    {
        await using var ctx = NewContext();
        var seededNames = await ctx.Permissions
            .IgnoreQueryFilters()
            .Where(p => p.Category == DocumentCategory
                     || p.Category == ExternalDocumentCategory
                     || p.Category == DocumentLinkCategory)
            .Select(p => p.Name)
            .ToListAsync(Ct);

        seededNames.Should().BeEquivalentTo(PermissionNames.Phase2Document);
    }

    [Fact]
    public async Task SeededPermissionCount_MatchesPhase2DocumentListAsync()
    {
        await using var ctx = NewContext();
        var count = await ctx.Permissions
            .IgnoreQueryFilters()
            .CountAsync(p => p.Category == DocumentCategory
                          || p.Category == ExternalDocumentCategory
                          || p.Category == DocumentLinkCategory, Ct);
        count.Should().Be(PermissionNames.Phase2Document.Count);
    }

    [Fact]
    public async Task SeededPermissions_HaveDeterministicCategoriesAsync()
    {
        // Pin the category distribution: 10 Document, 2 ExternalDocument,
        // 1 DocumentLink. A future addition that lands in the wrong
        // category bucket would silently mis-group in the admin UI.
        await using var ctx = NewContext();
        var byCategory = await ctx.Permissions
            .IgnoreQueryFilters()
            .Where(p => p.Category == DocumentCategory
                     || p.Category == ExternalDocumentCategory
                     || p.Category == DocumentLinkCategory)
            .GroupBy(p => p.Category)
            .Select(g => new { Category = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Category, g => g.Count, Ct);

        byCategory.Should().ContainKey(DocumentCategory).WhoseValue.Should().Be(10);
        byCategory.Should().ContainKey(ExternalDocumentCategory).WhoseValue.Should().Be(2);
        byCategory.Should().ContainKey(DocumentLinkCategory).WhoseValue.Should().Be(1);
    }

    [Fact]
    public async Task SeededPermissionIds_AreDeterministicAcrossInstallsAsync()
    {
        // Spot-check two specific Id ↔ Name pairings. The migration
        // commits the entire 13-row catalog to fixed Ids (prefix
        // 08000000-) so audit-log rows referencing PermissionId Guids
        // stay stable across environments. Spot-checking is sufficient
        // — the full mapping is reviewed at migration commit time.
        await using var ctx = NewContext();

        var documentCreate = await ctx.Permissions
            .IgnoreQueryFilters()
            .SingleAsync(p => p.Name == PermissionNames.DocumentCreate, Ct);
        documentCreate.Id.Should().Be(new Guid("08000000-0000-0000-0000-000000000001"));

        var documentLinkManage = await ctx.Permissions
            .IgnoreQueryFilters()
            .SingleAsync(p => p.Name == PermissionNames.DocumentLinkManage, Ct);
        documentLinkManage.Id.Should().Be(new Guid("08000000-0000-0000-0000-00000000000d"));
    }

    [Fact]
    public async Task SeededRows_AreNotSoftDeletedAsync()
    {
        await using var ctx = NewContext();
        var deletedCount = await ctx.Permissions
            .IgnoreQueryFilters()
            .CountAsync(p => (p.Category == DocumentCategory
                           || p.Category == ExternalDocumentCategory
                           || p.Category == DocumentLinkCategory)
                          && p.IsDeleted, Ct);
        deletedCount.Should().Be(0);
    }

    [Fact]
    public async Task QualityManagerRole_ExistsWithDeterministicIdAsync()
    {
        await using var ctx = NewContext();
        var qm = await ctx.Roles
            .IgnoreQueryFilters()
            .SingleAsync(r => r.Id == QualityManagerRoleId, Ct);
        qm.Name.Should().Be("QualityManager");
        qm.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task QualityManagerRolePermissions_MatchQualityManagerDefaultsExactlyAsync()
    {
        // The seeded QualityManager role has 12 RolePermission rows —
        // every Phase 2 document permission EXCEPT
        // Document.AssignReviewers. The set matches
        // PermissionNames.QualityManagerDefaults exactly.
        await using var ctx = NewContext();
        var qmPermissionNames = await (
            from rp in ctx.RolePermissions.IgnoreQueryFilters()
            join p in ctx.Permissions.IgnoreQueryFilters()
                on rp.PermissionId equals p.Id
            where rp.RoleId == QualityManagerRoleId
            select p.Name).ToListAsync(Ct);

        qmPermissionNames.Should().BeEquivalentTo(PermissionNames.QualityManagerDefaults);
    }

    [Fact]
    public async Task QualityManagerRole_DoesNotHaveDocumentAssignReviewersAsync()
    {
        // Load-bearing defensive assertion for the deliberate omission
        // per ADR 0008. Organizations grant Document.AssignReviewers
        // either to author roles (small-shop default) or restrict it
        // to QM (strict-gatekeeper); the seeded role must not pre-empt
        // either policy. If a future migration accidentally adds the
        // link, this test fires.
        await using var ctx = NewContext();

        var assignReviewersPermissionId = await ctx.Permissions
            .IgnoreQueryFilters()
            .Where(p => p.Name == PermissionNames.DocumentAssignReviewers)
            .Select(p => p.Id)
            .SingleAsync(Ct);

        var hasAssignReviewersGrant = await ctx.RolePermissions
            .IgnoreQueryFilters()
            .AnyAsync(rp =>
                rp.RoleId == QualityManagerRoleId
                && rp.PermissionId == assignReviewersPermissionId, Ct);

        hasAssignReviewersGrant.Should().BeFalse(
            "ADR 0008 leaves Document.AssignReviewers unassigned by " +
            "default — organizations grant it per their reviewer policy.");
    }

    [Fact]
    public async Task QualityManagerRolePermissions_AreAllOpenEndedAsync()
    {
        // The seeded grants are always-on (EffectiveToUtc null).
        // Time-bounded grants would imply an org policy decision the
        // seed doesn't make.
        await using var ctx = NewContext();
        var allOpenEnded = await ctx.RolePermissions
            .IgnoreQueryFilters()
            .Where(rp => rp.RoleId == QualityManagerRoleId)
            .AllAsync(rp => rp.EffectivePeriod.EffectiveToUtc == null, Ct);

        allOpenEnded.Should().BeTrue();
    }

    [Fact]
    public async Task QualityManagerRolePermissions_HaveSystemMigrationAttributionAsync()
    {
        // The migration's CreatedBy / ModifiedBy sentinel is
        // "system:migration" — the rows are not attributable to any
        // real user (the audit interceptor cannot fire on migration-
        // time inserts; the sentinel makes the source unambiguous in
        // the audit log if anyone queries by attribution later).
        await using var ctx = NewContext();
        var attributions = await ctx.RolePermissions
            .IgnoreQueryFilters()
            .Where(rp => rp.RoleId == QualityManagerRoleId)
            .Select(rp => rp.CreatedBy)
            .Distinct()
            .ToListAsync(Ct);

        attributions.Should().ContainSingle().Which.Should().Be("system:migration");
    }
}
