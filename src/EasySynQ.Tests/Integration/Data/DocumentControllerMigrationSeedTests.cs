using AwesomeAssertions;

using EasySynQ.Domain;
using EasySynQ.Domain.Entities.Identity;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace EasySynQ.Tests.Integration.Data;

/// <summary>
/// Verifies the Phase 2 document-controller seed shape — the
/// <c>AddDocumentControllerTables</c> migration (C1) plus the
/// <c>AddDocumentAuthorRoleAndAmendQualityManagerSeed</c> migration
/// (ADR 0011) — produces the permission catalog enumerated in
/// <see cref="PermissionNames.Phase2Document"/> and the two seeded
/// operational roles: <c>QualityManager</c> with
/// <see cref="PermissionNames.QualityManagerDefaults"/> (every Phase
/// 2 document permission including <c>Document.AssignReviewers</c>),
/// and <c>DocumentAuthor</c> with
/// <see cref="PermissionNames.DocumentAuthorDefaults"/> (the five
/// author-side permissions for the small-shop default).
/// </summary>
/// <remarks>
/// <para>
/// Drift between the migration's seed block and the code-side constants
/// would mean either a permission row that no code references (dead
/// data) or a code path checking a permission name that does not exist
/// (a bug). These tests pin the invariant at the migration level: the
/// migration chain is the catalog's source of truth, and the constants
/// must mirror it exactly.
/// </para>
/// <para>
/// The <c>QualityManagerRole_HasDocumentAssignReviewers</c> assertion
/// is load-bearing for ADR 0011's reversal of ADR 0008's deliberate
/// omission — if a future migration removes the grant, this test
/// fires. The corresponding negative assertion from the prior shape
/// (<c>DoesNotHaveDocumentAssignReviewers</c>) is gone; the new
/// positive assertion takes its place.
/// </para>
/// </remarks>
public class DocumentControllerMigrationSeedTests : IntegrationTestBase
{
    private const string DocumentCategory = "Document";
    private const string ExternalDocumentCategory = "ExternalDocument";
    private const string DocumentLinkCategory = "DocumentLink";

    private static readonly Guid QualityManagerRoleId =
        new("08100000-0000-0000-0000-000000000001");

    private static readonly Guid DocumentAuthorRoleId =
        new("08100000-0000-0000-0000-000000000002");

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
        // The seeded QualityManager role holds 13 RolePermission rows
        // after ADR 0011 amended the seed to include
        // Document.AssignReviewers — every Phase 2 document
        // permission. The set matches
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
    public async Task QualityManagerRole_HasDocumentAssignReviewersAsync()
    {
        // Load-bearing positive assertion for ADR 0011's reversal of
        // ADR 0008's deliberate omission. The
        // AddDocumentAuthorRoleAndAmendQualityManagerSeed migration
        // adds the QualityManager → Document.AssignReviewers
        // RolePermission link row; if a future migration removes it,
        // this test fires. Strict-gatekeeper deployments revoke
        // authoring permissions from the seeded DocumentAuthor role
        // instead of pruning QualityManager.
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

        hasAssignReviewersGrant.Should().BeTrue(
            "ADR 0011 amends the QualityManager seed to grant " +
            "Document.AssignReviewers — strict-gatekeeper deployments " +
            "revoke authoring permissions from DocumentAuthor instead.");
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

    // ─── DocumentAuthor (ADR 0011) ─────────────────────────────────

    [Fact]
    public async Task DocumentAuthorRole_ExistsWithDeterministicIdAsync()
    {
        await using var ctx = NewContext();
        var documentAuthor = await ctx.Roles
            .IgnoreQueryFilters()
            .SingleAsync(r => r.Id == DocumentAuthorRoleId, Ct);
        documentAuthor.Name.Should().Be("DocumentAuthor");
        documentAuthor.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task DocumentAuthorRolePermissions_MatchDocumentAuthorDefaultsExactlyAsync()
    {
        // The seeded DocumentAuthor role holds the five author-side
        // permissions per ADR 0011. The set matches
        // PermissionNames.DocumentAuthorDefaults exactly.
        await using var ctx = NewContext();
        var documentAuthorPermissionNames = await (
            from rp in ctx.RolePermissions.IgnoreQueryFilters()
            join p in ctx.Permissions.IgnoreQueryFilters()
                on rp.PermissionId equals p.Id
            where rp.RoleId == DocumentAuthorRoleId
            select p.Name).ToListAsync(Ct);

        documentAuthorPermissionNames.Should().BeEquivalentTo(
            PermissionNames.DocumentAuthorDefaults);
    }

    [Fact]
    public async Task DocumentAuthorRolePermissions_AreAllOpenEndedAsync()
    {
        // The seeded grants are always-on (EffectiveToUtc null).
        // Time-bounded grants would imply an org policy decision the
        // seed doesn't make.
        await using var ctx = NewContext();
        var allOpenEnded = await ctx.RolePermissions
            .IgnoreQueryFilters()
            .Where(rp => rp.RoleId == DocumentAuthorRoleId)
            .AllAsync(rp => rp.EffectivePeriod.EffectiveToUtc == null, Ct);

        allOpenEnded.Should().BeTrue();
    }

    [Fact]
    public async Task DocumentAuthorRolePermissions_HaveSystemMigrationAttributionAsync()
    {
        // Same attribution discipline as the QualityManager rows —
        // migration-time inserts bypass the audit interceptor and
        // the "system:migration" sentinel makes the source
        // unambiguous in any future audit query.
        await using var ctx = NewContext();
        var attributions = await ctx.RolePermissions
            .IgnoreQueryFilters()
            .Where(rp => rp.RoleId == DocumentAuthorRoleId)
            .Select(rp => rp.CreatedBy)
            .Distinct()
            .ToListAsync(Ct);

        attributions.Should().ContainSingle().Which.Should().Be("system:migration");
    }

    // ─── Migration-applies-cleanly row-delta invariant (ADR 0011) ──

    [Fact]
    public async Task AddDocumentAuthorRoleAndAmendQualityManagerSeed_ProducesExpectedRowDeltasAsync()
    {
        // The migration adds exactly:
        //   - 1 Role row (DocumentAuthor)
        //   - 6 RolePermission rows (5 DocumentAuthor links + 1
        //     QualityManager → AssignReviewers amendment)
        // The test base's IntegrationTestBase already applied the full
        // migration chain; this test verifies the post-state matches
        // those counts when filtered to the rows the new migration
        // owns (by deterministic Id). No duplicate rows; no
        // rewritten existing rows.
        await using var ctx = NewContext();

        // 1 Role row owned by the new migration.
        var newRoleIds = new[] { DocumentAuthorRoleId };
        var newRolesPresent = await ctx.Roles
            .IgnoreQueryFilters()
            .CountAsync(r => newRoleIds.Contains(r.Id), Ct);
        newRolesPresent.Should().Be(1);

        // 6 RolePermission rows owned by the new migration.
        var newRolePermissionIds = new[]
        {
            new Guid("08200000-0000-0000-0000-000000000004"), // QM → AssignReviewers
            new Guid("08210000-0000-0000-0000-000000000001"), // DA → Create
            new Guid("08210000-0000-0000-0000-000000000002"), // DA → EditDraft
            new Guid("08210000-0000-0000-0000-000000000003"), // DA → SubmitForReview
            new Guid("08210000-0000-0000-0000-000000000004"), // DA → AssignReviewers
            new Guid("08210000-0000-0000-0000-000000000009"), // DA → HardDelete
        };
        var newRolePermissionsPresent = await ctx.RolePermissions
            .IgnoreQueryFilters()
            .CountAsync(rp => newRolePermissionIds.Contains(rp.Id), Ct);
        newRolePermissionsPresent.Should().Be(6);

        // No row-duplication: total RolePermissions for the affected
        // roles equals (12 QM rows from C1 + 1 new QM row) + 5 new
        // DA rows = 18.
        var totalForAffectedRoles = await ctx.RolePermissions
            .IgnoreQueryFilters()
            .CountAsync(rp => rp.RoleId == QualityManagerRoleId
                           || rp.RoleId == DocumentAuthorRoleId, Ct);
        totalForAffectedRoles.Should().Be(18);
    }
}
