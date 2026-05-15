using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasySynQ.Data.Migrations
{
    /// <summary>
    /// Adds the <c>Vault.PhysicalDelete</c> system permission to the
    /// catalog and — if an Administrator role exists at migration time —
    /// seeds a <c>RolePermission</c> link row granting that permission
    /// to the Administrator role (ADR 0008 C2). Single-purpose
    /// migration; mirrors the
    /// <see cref="LinkLegacyAdministratorToSystemPermissions"/> shape
    /// for the conditional administrator link.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Fresh install path.</b> Migrations run before bootstrap, so
    /// no Administrator role exists when this migration applies. The
    /// Permission row is inserted; the RolePermission link is skipped
    /// (the <c>WHERE EXISTS</c> clause matches zero rows). When
    /// bootstrap subsequently runs <c>CreateAdministratorAsync</c>, it
    /// fetches <c>PermissionNames.All</c> — which now includes
    /// <c>Vault.PhysicalDelete</c> — and writes the full set of
    /// RolePermission rows for the new Administrator. The
    /// <c>Vault.PhysicalDelete</c> link is included in that
    /// bootstrap-time write, matching every other system permission.
    /// </para>
    /// <para>
    /// <b>Upgrade install path.</b> An Administrator role already
    /// exists (created by a prior bootstrap). The Permission row is
    /// inserted; the conditional INSERT-SELECT writes the
    /// RolePermission link row attributing it to
    /// <c>CreatedBy = "system:migration"</c>, mirroring the upgrade
    /// pattern established by
    /// <c>LinkLegacyAdministratorToSystemPermissions</c>. The
    /// Administrator's effective permission set picks up
    /// <c>Vault.PhysicalDelete</c> on the next sign-in.
    /// </para>
    /// <para>
    /// <b>Deterministic Ids.</b> Permission row at
    /// <c>08300000-0000-0000-0000-000000000001</c> (reserves the 083-
    /// prefix for future vault permissions); RolePermission link at
    /// <c>08400000-0000-0000-0000-000000000001</c> (reserves 084- for
    /// vault-related role-permission links). Mirrors the Phase 2 C1
    /// convention (08- for the Phase 2 catalog overall, with sub-
    /// prefixes per section).
    /// </para>
    /// </remarks>
    [SuppressMessage(
        "Naming",
        "CA1711:Identifiers should not have incorrect suffix",
        Justification = "EF Core migration class names are file-name-derived identifiers describing the migration's effect; the 'Permission' suffix is the spec-prescribed naming for this migration's scope (adds the Vault.PhysicalDelete permission). Renaming would diverge from the migration's intent.")]
    public partial class AddVaultPhysicalDeletePermission : Migration
    {
        private static readonly Guid VaultPhysicalDeletePermissionId =
            new("08300000-0000-0000-0000-000000000001");

        private static readonly Guid AdministratorVaultRolePermissionId =
            new("08400000-0000-0000-0000-000000000001");

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Capture run timestamp once so the Permission row's
            // CreatedUtc/ModifiedUtc and the RolePermission row's
            // EffectiveFromUtc/CreatedUtc/ModifiedUtc share one
            // instant. DateTime.UtcNow is used directly because
            // migrations operate outside the DI container and IClock
            // is not available here.
            var runUtc = DateTime.UtcNow;
            var runUtcSql = runUtc.ToString(
                "yyyy-MM-dd HH:mm:ss.fffffff",
                CultureInfo.InvariantCulture);

            // Step 1 — seed the Permission row. Unconditional; the
            // catalog grows the same way on every install.
            migrationBuilder.InsertData(
                table: "Permissions",
                columns: new[]
                {
                    "Id", "Name", "Description", "Category",
                    "CreatedBy", "CreatedUtc", "ModifiedBy", "ModifiedUtc",
                    "RowVersion", "IsDeleted",
                },
                values: new object[,]
                {
                    {
                        VaultPhysicalDeletePermissionId,
                        "Vault.PhysicalDelete",
                        "Physically delete vault blobs (hard-delete the row + remove the on-disk file). System-administration capability granted to the Administrator role.",
                        "System",
                        "system:migration", runUtc, "system:migration", runUtc,
                        new byte[8], false,
                    },
                });

            // Step 2 — conditionally link the Administrator role to
            // the new permission. INSERT-SELECT writes 0 rows on a
            // fresh install (no Administrator yet) and 1 row on
            // upgrade (Administrator exists). The
            // AdministratorVaultRolePermissionId is deterministic so
            // audit trails referencing the row by Id are stable
            // across upgrade installs.
            //
            // EffectiveFromUtc set to runUtc; EffectiveToUtc null
            // (open-ended). RowVersion is the all-zeros sentinel
            // matching every other migration-time insert; the
            // interceptor assigns a real value on first EF-tracked
            // modification.
            var permissionIdSql = VaultPhysicalDeletePermissionId
                .ToString("D", CultureInfo.InvariantCulture)
                .ToLowerInvariant();
            var rolePermissionIdSql = AdministratorVaultRolePermissionId
                .ToString("D", CultureInfo.InvariantCulture)
                .ToLowerInvariant();

            migrationBuilder.Sql(string.Create(CultureInfo.InvariantCulture, $@"
                INSERT INTO RolePermissions (
                    Id, RoleId, PermissionId,
                    EffectiveFromUtc, EffectiveToUtc,
                    CreatedBy, CreatedUtc, ModifiedBy, ModifiedUtc,
                    RowVersion, IsDeleted
                )
                SELECT
                    '{rolePermissionIdSql}',
                    r.Id,
                    '{permissionIdSql}',
                    '{runUtcSql}', NULL,
                    'system:migration', '{runUtcSql}', 'system:migration', '{runUtcSql}',
                    x'0000000000000000', 0
                FROM Roles r
                WHERE r.Name = 'Administrator'
                    AND NOT EXISTS (
                        SELECT 1 FROM RolePermissions
                        WHERE RoleId = r.Id AND PermissionId = '{permissionIdSql}'
                    );
            "));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Symmetric with Up. Delete the link row first (FK
            // dependency), then the Permission row.
            var permissionIdSql = VaultPhysicalDeletePermissionId
                .ToString("D", CultureInfo.InvariantCulture)
                .ToLowerInvariant();

            migrationBuilder.Sql(string.Create(CultureInfo.InvariantCulture, $@"
                DELETE FROM RolePermissions
                WHERE PermissionId = '{permissionIdSql}'
                    AND CreatedBy = 'system:migration';
            "));

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: VaultPhysicalDeletePermissionId);
        }
    }
}
