using AwesomeAssertions;

using EasySynQ.Domain;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace EasySynQ.Tests.Integration.Data;

/// <summary>
/// Verifies the <c>AddVaultPhysicalDeletePermission</c> migration seeds
/// the <c>Vault.PhysicalDelete</c> Permission row at the deterministic
/// Id and (on fresh installs) skips the Administrator RolePermission
/// link because no Administrator role exists at migration time.
/// </summary>
/// <remarks>
/// <para>
/// The upgrade-install behavior (Administrator role pre-exists; the
/// migration writes the RolePermission link) is exercised by the
/// pre-existing
/// <see cref="LinkLegacyAdministratorMigrationTests"/> pattern; the
/// shape mirrors that test's targeted-migration approach. C2 doesn't
/// add a dedicated upgrade-path test because the seeding logic is a
/// simple conditional INSERT-SELECT — the more interesting upgrade
/// shape (loud-failure trigger on partial state) belongs to
/// LinkLegacy and doesn't apply here.
/// </para>
/// <para>
/// The bootstrap-time behavior (Administrator gets
/// <c>Vault.PhysicalDelete</c> because it's in
/// <see cref="PermissionNames.All"/>) is covered by the audit-row
/// count + permission-snapshot assertions in
/// <c>BootstrapServiceTests</c>; PermissionNames.All.Count was
/// expanded to 12 by this commit, so those tests automatically
/// validate the new permission's bootstrap inclusion.
/// </para>
/// </remarks>
public class AddVaultPhysicalDeletePermissionMigrationSeedTests : IntegrationTestBase
{
    private static readonly Guid VaultPhysicalDeletePermissionId =
        new("08300000-0000-0000-0000-000000000001");

    private static readonly Guid AdministratorVaultRolePermissionId =
        new("08400000-0000-0000-0000-000000000001");

    [Fact]
    public async Task SeededPermissionRow_ExistsWithDeterministicIdAsync()
    {
        await using var ctx = NewContext();
        var permission = await ctx.Permissions
            .IgnoreQueryFilters()
            .SingleAsync(p => p.Id == VaultPhysicalDeletePermissionId, Ct);

        permission.Name.Should().Be(PermissionNames.VaultPhysicalDelete);
        permission.Category.Should().Be("System");
        permission.CreatedBy.Should().Be("system:migration");
        permission.ModifiedBy.Should().Be("system:migration");
        permission.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task SeededPermission_AppearsInPermissionNamesAllAsync()
    {
        // The constant must be present in the canonical bootstrap list
        // — that's the signal that the new permission rolls into
        // every fresh install's Administrator grants without further
        // code changes.
        PermissionNames.All.Should().Contain(PermissionNames.VaultPhysicalDelete);
    }

    [Fact]
    public async Task FreshInstall_NoAdministrator_NoRolePermissionLinkAsync()
    {
        // The test fixture's DB is migrated end-to-end through all
        // migrations, including this one. No Administrator role
        // exists at any point during the migration run (bootstrap
        // creates it at runtime, after migrations complete). The
        // conditional INSERT-SELECT in this migration matched zero
        // rows, so no link row exists.
        await using var ctx = NewContext();
        var hasLink = await ctx.RolePermissions
            .IgnoreQueryFilters()
            .AnyAsync(rp => rp.Id == AdministratorVaultRolePermissionId, Ct);
        hasLink.Should().BeFalse();
    }

    [Fact]
    public async Task SystemPermissionCount_NowTwelveAsync()
    {
        // Cumulative invariant check: the System-category catalog
        // should have grown from 11 (Phase 1) to 12 (Phase 2 C2's
        // vault permission). Scoped to Category=="System" per the
        // narrow-scoping lesson; future Phase additions to other
        // categories (Document/etc.) do not affect this count.
        await using var ctx = NewContext();
        var systemCount = await ctx.Permissions
            .IgnoreQueryFilters()
            .CountAsync(p => p.Category == "System", Ct);
        systemCount.Should().Be(PermissionNames.All.Count);
    }
}
