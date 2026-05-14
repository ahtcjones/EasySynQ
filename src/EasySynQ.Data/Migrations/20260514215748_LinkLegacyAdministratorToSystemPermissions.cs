using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using EasySynQ.Domain;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasySynQ.Data.Migrations
{
    /// <summary>
    /// Closes the ADR 0007 upgrade-path gap surfaced during the
    /// 2026-05-14 C3 smoke. The previous migration
    /// (<c>AddPermissionsAndLinkTables</c>) seeds the eleven Phase 1
    /// system <see cref="EasySynQ.Domain.Entities.Identity.Permission"/>
    /// rows into the catalog but does not write
    /// <see cref="EasySynQ.Domain.Entities.Identity.RolePermission"/>
    /// link rows for an Administrator role that pre-existed the
    /// migration (created by an F1-era bootstrap before ADR 0007 was
    /// implemented). On such an install the legacy admin upgrades to
    /// <c>Permissions: []</c> and every authorization check fails.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Fresh installs are unaffected. On a fresh install this migration
    /// runs in the initial migration batch BEFORE
    /// <c>IBootstrapService.CreateAdministratorAsync</c>; finds no
    /// Administrator role; no-ops. Bootstrap then writes the link rows
    /// itself transactionally — same path as a post-ADR-0007 fresh
    /// install with this migration absent.
    /// </para>
    /// <para>
    /// On a legacy install this migration finds the Administrator role
    /// with zero RolePermission rows and writes the eleven link rows
    /// in one statement, attributed to <c>CreatedBy = "system:migration"</c>
    /// (matching the sentinel <c>AddPermissionsAndLinkTables</c> uses
    /// for its seeded Permission rows). The migration's git history is
    /// its audit trail; migration-time inserts bypass the audit
    /// interceptor by design.
    /// </para>
    /// <para>
    /// <b>Loud failure on partial state.</b> If the Administrator role
    /// exists with one or more RolePermission rows pre-existing, this
    /// migration aborts via a temporary SQLite trigger that raises a
    /// diagnostic message. The condition is unreachable in either
    /// supported path (fresh install: bootstrap writes 11 transactionally
    /// after this migration; legacy install: zero pre-existing rows).
    /// A loud abort surfaces any data anomaly for human investigation
    /// rather than silently no-opping and leaving the partial state in
    /// place. See ADR 0007 and the 2026-05-14 entry in
    /// <c>docs/SESSION_NOTES.md</c> for context.
    /// </para>
    /// </remarks>
    public partial class LinkLegacyAdministratorToSystemPermissions : Migration
    {
        // The diagnostic that surfaces via SqliteException.Message when
        // the trigger fires. Single line because SQLite RAISE messages
        // are single-quoted SQL string literals.
        private const string PartialStateDiagnostic =
            "LinkLegacyAdministratorToSystemPermissions: precondition violation — the Administrator role already has one or more RolePermission rows. This migration expects zero pre-existing link rows for the Administrator role. A human must investigate before proceeding. See ADR 0007 and the 2026-05-14 entry in docs/SESSION_NOTES.md.";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Capture run timestamp once so all eleven inserted rows
            // share one EffectiveFromUtc and one CreatedUtc / ModifiedUtc.
            // DateTime.UtcNow is used directly because migrations operate
            // outside the DI container and IClock is not available here.
            var runUtc = DateTime.UtcNow;
            var runUtcSql = runUtc.ToString("yyyy-MM-dd HH:mm:ss.fffffff", CultureInfo.InvariantCulture);

            // Generate one fresh Guid per RolePermission row. Per-install
            // uniqueness only; no determinism requirement (these are
            // link rows specific to this install's Administrator-role Id,
            // which is itself per-install). Keyed by Permission.Name so
            // the CASE expression in the INSERT-SELECT can map each
            // catalog row to its pre-generated Id deterministically
            // within this single migration run.
            var rolePermissionIds = new Dictionary<string, Guid>(PermissionNames.All.Count);
            foreach (var name in PermissionNames.All)
            {
                rolePermissionIds[name] = Guid.NewGuid();
            }

            // Step 1 — install the partial-state guard table.
            // TEMP TABLE / TEMP TRIGGER are connection-scoped and
            // auto-cleaned when the connection closes; this migration's
            // entire Up() runs on one connection so no explicit cleanup
            // is required if the migration aborts.
            migrationBuilder.Sql(
                "CREATE TEMP TABLE __MigrationGuardLinkLegacy (placeholder INTEGER);");

            // Step 2 — install the trigger. RAISE(ABORT, msg) inside a
            // BEFORE INSERT trigger surfaces `msg` as the
            // SqliteException.Message when EF Core runs the migration,
            // so the operator sees the diagnostic directly without
            // having to dig through wrapped inner exceptions.
            migrationBuilder.Sql(string.Create(CultureInfo.InvariantCulture, $@"
                CREATE TEMP TRIGGER __MigrationGuardLinkLegacy_RaisePartialState
                BEFORE INSERT ON __MigrationGuardLinkLegacy
                BEGIN
                    SELECT RAISE(ABORT, '{PartialStateDiagnostic}');
                END;
            "));

            // Step 3 — the partial-state check. Inserts into the guard
            // table iff at least one RolePermission row exists for the
            // Administrator role; the trigger fires and aborts the
            // entire transaction. If no Administrator role exists, or
            // the role exists with zero link rows, this INSERT is a
            // no-op (WHERE EXISTS evaluates false → zero rows selected
            // → trigger not fired).
            migrationBuilder.Sql(@"
                INSERT INTO __MigrationGuardLinkLegacy(placeholder)
                SELECT 1
                WHERE EXISTS (
                    SELECT 1 FROM Roles r
                    JOIN RolePermissions rp ON rp.RoleId = r.Id
                    WHERE r.Name = 'Administrator'
                );
            ");

            // Step 4 — the real work. CROSS JOIN Roles × Permissions
            // restricted to the Administrator role and the eleven
            // Phase 1 catalog rows, producing 0 rows when Administrator
            // is absent (fresh-install path) and 11 rows when it's
            // present with zero pre-existing link rows (legacy-install
            // path). The CASE expression maps each Permission.Name to
            // its pre-generated RolePermission.Id. NOT EXISTS is
            // defense-in-depth — step 3's trigger already aborts if
            // any link rows exist, so step 4 only runs in the
            // zero-link-rows or no-Administrator cases.
            var caseArms = string.Join(
                Environment.NewLine,
                rolePermissionIds.Select(kv =>
                    $"                        WHEN '{kv.Key}' THEN '{kv.Value.ToString("D", CultureInfo.InvariantCulture).ToLowerInvariant()}'"));
            var permissionNamesInClause = string.Join(
                ", ",
                PermissionNames.All.Select(n => $"'{n}'"));

            migrationBuilder.Sql(string.Create(CultureInfo.InvariantCulture, $@"
                INSERT INTO RolePermissions (
                    Id, RoleId, PermissionId,
                    EffectiveFromUtc, EffectiveToUtc,
                    CreatedBy, CreatedUtc, ModifiedBy, ModifiedUtc,
                    RowVersion, IsDeleted
                )
                SELECT
                    CASE p.Name
{caseArms}
                    END,
                    r.Id,
                    p.Id,
                    '{runUtcSql}', NULL,
                    'system:migration', '{runUtcSql}', 'system:migration', '{runUtcSql}',
                    x'0000000000000000', 0
                FROM Roles r
                CROSS JOIN Permissions p
                WHERE r.Name = 'Administrator'
                    AND p.Name IN ({permissionNamesInClause})
                    AND NOT EXISTS (SELECT 1 FROM RolePermissions WHERE RoleId = r.Id);
            "));

            // Step 5 — drop the guard table. Skipped (along with this
            // migration's __EFMigrationsHistory write) if step 3
            // aborted the transaction. TEMP-scoped cleanup happens
            // anyway when the migrator's connection closes; this is
            // belt-and-braces for the happy path.
            migrationBuilder.Sql("DROP TABLE __MigrationGuardLinkLegacy;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Symmetric with Up: delete only the RolePermission rows
            // this migration wrote. The CreatedBy = "system:migration"
            // filter matches the sentinel Up sets on every inserted
            // row, distinguishing them from any rows BootstrapService
            // or a future admin tool might write under a real user's
            // attribution.
            migrationBuilder.Sql(@"
                DELETE FROM RolePermissions
                WHERE RoleId IN (SELECT Id FROM Roles WHERE Name = 'Administrator')
                    AND CreatedBy = 'system:migration';
            ");
        }
    }
}
