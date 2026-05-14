using AwesomeAssertions;

using EasySynQ.Data.Context;
using EasySynQ.Domain;
using EasySynQ.Domain.Entities.Identity;
using EasySynQ.Domain.ValueObjects;
using EasySynQ.Services.Abstractions;
using EasySynQ.Services.Time;
using EasySynQ.Tests.TestHelpers;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Xunit;

namespace EasySynQ.Tests.Integration.Data;

/// <summary>
/// Exercises the <c>LinkLegacyAdministratorToSystemPermissions</c>
/// migration against the three pre-states it must handle:
/// fresh-shape (no Administrator role), legacy-shape (Administrator
/// role with zero RolePermission rows), and partial-state (one or
/// more RolePermission rows pre-existing). The migration closes the
/// ADR 0007 upgrade-path gap surfaced by the 2026-05-14 C3 smoke;
/// see the migration file's header docs and the dated handoff entry.
/// </summary>
/// <remarks>
/// Each test creates a fresh temp DB migrated only as far as
/// <c>AddPermissionsAndLinkTables</c> (the migration before the one
/// under test), seeds the pre-state directly, then applies the
/// pending migration via <c>ctx.Database.Migrate()</c> and asserts
/// the post-state. The pattern mirrors what the production
/// <c>App.ApplyPendingMigrations</c> startup path would do: any
/// pending migration runs on next launch.
/// </remarks>
public class LinkLegacyAdministratorMigrationTests : IDisposable
{
    private const string PriorMigrationName = "AddPermissionsAndLinkTables";

    private readonly string _dbPath;
    private readonly DbContextOptions<EasySynQDbContext> _options;
    private readonly ITemporalResolver _temporalResolver;
    private bool _disposed;

    public LinkLegacyAdministratorMigrationTests()
    {
        (_dbPath, _options) = TempSqliteDb.CreateMigratedTo(PriorMigrationName);
        _temporalResolver = new CurrentTimeTemporalResolver(new SystemClock());
    }

    private EasySynQDbContext NewContext() => new(_options, _temporalResolver);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task LegacyShape_WritesElevenLinkRowsForAdministratorAsync()
    {
        // Seed the legacy-shape pre-state: an Administrator role and a
        // UserRole assignment exist (an F1-era bootstrap would have
        // written these), but no RolePermission rows do.
        var adminRoleId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using (var ctx = NewContext())
        {
            ctx.Roles.Add(new Role(
                adminRoleId,
                "Administrator",
                "F1-era administrator role for the legacy-shape test."));
            ctx.Users.Add(new User(
                userId, "admin", "Legacy Admin",
                "hash", "salt", 1000, mustChangePassword: false));
            ctx.UserRoles.Add(new UserRole(
                Guid.NewGuid(), userId, adminRoleId,
                new EffectiveDateRange(
                    new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    null)));
            await ctx.SaveChangesAsync(Ct);
        }

        // Sanity-check pre-state: zero RolePermission rows.
        await using (var ctx = NewContext())
        {
            (await ctx.RolePermissions.CountAsync(Ct)).Should().Be(0);
        }

        // Apply the pending migration (only LinkLegacyAdministrator…
        // remains).
        await using (var ctx = NewContext())
        {
            ctx.Database.Migrate();
        }

        // Post-state assertions.
        await using (var ctx = NewContext())
        {
            var rolePermissions = await ctx.RolePermissions
                .IgnoreQueryFilters()
                .ToListAsync(Ct);

            rolePermissions.Should().HaveCount(PermissionNames.All.Count);
            rolePermissions.Should().OnlyContain(rp => rp.RoleId == adminRoleId);
            rolePermissions.Should().OnlyContain(rp => rp.EffectivePeriod.EffectiveToUtc == null);
            rolePermissions.Should().OnlyContain(rp => rp.CreatedBy == "system:migration");
            rolePermissions.Should().OnlyContain(rp => rp.ModifiedBy == "system:migration");

            // The eleven linked Permission.Name values match
            // PermissionNames.All exactly (the catalog seeded by the
            // prior migration).
            var linkedNames = await (
                from rp in ctx.RolePermissions.IgnoreQueryFilters()
                join p in ctx.Permissions.IgnoreQueryFilters()
                    on rp.PermissionId equals p.Id
                select p.Name).ToListAsync(Ct);

            linkedNames.Should().BeEquivalentTo(PermissionNames.All);
        }
    }

    [Fact]
    public async Task FreshShape_NoAdministratorRole_IsNoOpAsync()
    {
        // Fresh-install pre-state: no Roles, no Users, no link rows.
        // (Permissions exist — seeded by the prior migration.)
        await using (var ctx = NewContext())
        {
            (await ctx.Roles.CountAsync(Ct)).Should().Be(0);
            (await ctx.RolePermissions.CountAsync(Ct)).Should().Be(0);
        }

        // Apply the pending migration.
        await using (var ctx = NewContext())
        {
            ctx.Database.Migrate();
        }

        // Post-state: still zero RolePermission rows, no exception.
        // (The bootstrap path writes the link rows on first launch;
        // this migration's job is only to fix legacy installs.)
        await using (var ctx = NewContext())
        {
            (await ctx.RolePermissions.IgnoreQueryFilters().CountAsync(Ct)).Should().Be(0);
        }
    }

    [Fact]
    public async Task PartialState_AdministratorHasSomeLinkRows_ThrowsAsync()
    {
        // Seed a partial-state pre-condition: Administrator role exists
        // with exactly one RolePermission row (linked to
        // System.Administer only — the other ten link rows are absent).
        // This shape is unreachable in either supported flow (fresh
        // bootstrap writes all 11 transactionally; legacy installs have
        // zero); the test exists to pin the defensive abort.
        var adminRoleId = Guid.NewGuid();
        await using (var ctx = NewContext())
        {
            ctx.Roles.Add(new Role(
                adminRoleId,
                "Administrator",
                "Partial-state pre-condition for the abort test."));

            var systemAdministerPermissionId = await ctx.Permissions
                .Where(p => p.Name == PermissionNames.SystemAdminister)
                .Select(p => p.Id)
                .SingleAsync(Ct);

            ctx.RolePermissions.Add(new RolePermission(
                Guid.NewGuid(),
                adminRoleId,
                systemAdministerPermissionId,
                new EffectiveDateRange(
                    new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    null)));
            await ctx.SaveChangesAsync(Ct);
        }

        // Apply the pending migration. The temp-trigger guard inside
        // the migration's Up() fires RAISE(ABORT, '…') when the
        // partial-state precondition is detected; SqliteException
        // surfaces with the diagnostic in its Message.
        var act = () =>
        {
            using var ctx = NewContext();
            ctx.Database.Migrate();
        };

        act.Should()
            .Throw<SqliteException>()
            .Where(ex =>
                ex.Message.Contains("LinkLegacyAdministratorToSystemPermissions", StringComparison.Ordinal)
                && ex.Message.Contains("precondition violation", StringComparison.Ordinal));

        // Post-state: no additional RolePermission rows were written.
        // The partial state remains exactly as seeded — the migration
        // aborted before the real INSERT could run.
        await using (var ctx = NewContext())
        {
            (await ctx.RolePermissions.IgnoreQueryFilters().CountAsync(Ct)).Should().Be(1);
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            TempSqliteDb.Delete(_dbPath);
        }

        _disposed = true;
    }
}
