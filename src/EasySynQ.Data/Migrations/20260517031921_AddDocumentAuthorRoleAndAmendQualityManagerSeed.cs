using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasySynQ.Data.Migrations
{
    /// <summary>
    /// Reconciles the Phase 2 seed shape with the deployment-model
    /// intent surfaced at C6b stop 9 (ADR 0011): author-can-submit is
    /// the small-shop default, admin is the IT-side seat (not an
    /// operational user). Seeds a generic <c>DocumentAuthor</c> role
    /// with the five author-side permissions and amends the seeded
    /// <c>QualityManager</c> role to grant
    /// <c>Document.AssignReviewers</c> (the previously-omitted slot
    /// from <see cref="AddDocumentControllerTables"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Fresh install path.</b> The C1 migration
    /// <see cref="AddDocumentControllerTables"/> seeds the Phase 2
    /// permission catalog and the <c>QualityManager</c> role row;
    /// this migration runs strictly after, so it can rely on those
    /// rows existing. No upgrade-path conditional is needed —
    /// straightforward <c>InsertData</c> calls suffice.
    /// </para>
    /// <para>
    /// <b>Deterministic Ids.</b> <c>DocumentAuthor</c>'s
    /// <c>Role.Id</c> takes slot 02 of the existing <c>08100000-</c>
    /// prefix (slot 01 is <c>QualityManager</c>). The
    /// <c>DocumentAuthor → permission</c> link rows use a new prefix
    /// (<c>08210000-</c>) so the suffix can echo each permission's
    /// own suffix for visual tracing in raw queries. The new
    /// <c>QualityManager → Document.AssignReviewers</c> link row
    /// fills slot 04 of the existing <c>08200000-</c> prefix — the
    /// slot C1 deliberately skipped.
    /// </para>
    /// <para>
    /// <b>Audit attribution.</b> Migration-time inserts bypass the
    /// audit interceptor by design (ADR 0007 precedent); the rows
    /// are attributed to <c>CreatedBy = "system:migration"</c>
    /// matching every other catalog seed migration. The migration's
    /// git history is its audit trail.
    /// </para>
    /// </remarks>
    public partial class AddDocumentAuthorRoleAndAmendQualityManagerSeed : Migration
    {
        // Existing role id from the C1 migration — referenced by the
        // new QualityManager → Document.AssignReviewers link.
        private static readonly Guid QualityManagerRoleId =
            new("08100000-0000-0000-0000-000000000001");

        // New role id (slot 02 of the 08100000- prefix).
        private static readonly Guid DocumentAuthorRoleId =
            new("08100000-0000-0000-0000-000000000002");

        // Existing permission ids from the C1 migration — referenced
        // by the new RolePermission link rows.
        private static readonly Guid DocumentCreatePermissionId =
            new("08000000-0000-0000-0000-000000000001");

        private static readonly Guid DocumentEditDraftPermissionId =
            new("08000000-0000-0000-0000-000000000002");

        private static readonly Guid DocumentSubmitForReviewPermissionId =
            new("08000000-0000-0000-0000-000000000003");

        private static readonly Guid DocumentAssignReviewersPermissionId =
            new("08000000-0000-0000-0000-000000000004");

        private static readonly Guid DocumentHardDeletePermissionId =
            new("08000000-0000-0000-0000-000000000009");

        // New QualityManager → Document.AssignReviewers RolePermission
        // id. Fills slot 04 of the 08200000- prefix (the slot C1's
        // migration deliberately skipped to leave room for this link).
        private static readonly Guid QualityManagerAssignReviewersRolePermissionId =
            new("08200000-0000-0000-0000-000000000004");

        // New DocumentAuthor → permission RolePermission ids
        // (08210000- prefix; suffix matches each permission's own
        // suffix).
        private static readonly Guid DocumentAuthorCreateRolePermissionId =
            new("08210000-0000-0000-0000-000000000001");

        private static readonly Guid DocumentAuthorEditDraftRolePermissionId =
            new("08210000-0000-0000-0000-000000000002");

        private static readonly Guid DocumentAuthorSubmitForReviewRolePermissionId =
            new("08210000-0000-0000-0000-000000000003");

        private static readonly Guid DocumentAuthorAssignReviewersRolePermissionId =
            new("08210000-0000-0000-0000-000000000004");

        private static readonly Guid DocumentAuthorHardDeleteRolePermissionId =
            new("08210000-0000-0000-0000-000000000009");

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Capture run timestamp once so all rows share one
            // CreatedUtc / ModifiedUtc / EffectiveFromUtc instant.
            // DateTime.UtcNow is used directly because migrations
            // operate outside the DI container and IClock is not
            // available here (matches the AddVaultPhysicalDeletePermission
            // and LinkLegacyAdministratorToSystemPermissions pattern).
            var runUtc = DateTime.UtcNow;
            var seedRowVersion = new byte[8];
            const string seedBy = "system:migration";

            // Step 1 — seed the new DocumentAuthor role.
            migrationBuilder.InsertData(
                table: "Roles",
                columns: new[]
                {
                    "Id", "Name", "Description",
                    "CreatedBy", "CreatedUtc", "ModifiedBy", "ModifiedUtc",
                    "RowVersion", "IsDeleted",
                },
                values: new object[,]
                {
                    {
                        DocumentAuthorRoleId,
                        "DocumentAuthor",
                        "Generic operational role for users whose primary responsibility is authoring controlled documents. Seeded by Phase 2's migration chain per ADR 0011. Holds Document.Create, Document.EditDraft, Document.HardDelete, Document.SubmitForReview, and Document.AssignReviewers — the author-can-submit small-shop default. Organizations rename, reshape, or replace this role via admin UI when it ships.",
                        seedBy, runUtc, seedBy, runUtc,
                        seedRowVersion, false,
                    },
                });

            // Step 2 — seed the five DocumentAuthor → permission
            // RolePermission link rows. Each row's Id suffix echoes
            // the permission's own suffix for visual tracing.
            // EffectiveFromUtc = runUtc; EffectiveToUtc = null
            // (open-ended) per ADR 0011's "seeded grants are
            // always-on" decision.
            migrationBuilder.InsertData(
                table: "RolePermissions",
                columns: new[]
                {
                    "Id", "RoleId", "PermissionId",
                    "EffectiveFromUtc", "EffectiveToUtc",
                    "CreatedBy", "CreatedUtc", "ModifiedBy", "ModifiedUtc",
                    "RowVersion", "IsDeleted",
                },
                values: new object[,]
                {
                    {
                        DocumentAuthorCreateRolePermissionId,
                        DocumentAuthorRoleId,
                        DocumentCreatePermissionId,
                        runUtc, (DateTime?)null,
                        seedBy, runUtc, seedBy, runUtc,
                        seedRowVersion, false,
                    },
                    {
                        DocumentAuthorEditDraftRolePermissionId,
                        DocumentAuthorRoleId,
                        DocumentEditDraftPermissionId,
                        runUtc, (DateTime?)null,
                        seedBy, runUtc, seedBy, runUtc,
                        seedRowVersion, false,
                    },
                    {
                        DocumentAuthorSubmitForReviewRolePermissionId,
                        DocumentAuthorRoleId,
                        DocumentSubmitForReviewPermissionId,
                        runUtc, (DateTime?)null,
                        seedBy, runUtc, seedBy, runUtc,
                        seedRowVersion, false,
                    },
                    {
                        DocumentAuthorAssignReviewersRolePermissionId,
                        DocumentAuthorRoleId,
                        DocumentAssignReviewersPermissionId,
                        runUtc, (DateTime?)null,
                        seedBy, runUtc, seedBy, runUtc,
                        seedRowVersion, false,
                    },
                    {
                        DocumentAuthorHardDeleteRolePermissionId,
                        DocumentAuthorRoleId,
                        DocumentHardDeletePermissionId,
                        runUtc, (DateTime?)null,
                        seedBy, runUtc, seedBy, runUtc,
                        seedRowVersion, false,
                    },
                });

            // Step 3 — amend the QualityManager role's permission set
            // to include Document.AssignReviewers. One new
            // RolePermission row filling slot 04 of the 08200000-
            // prefix (the slot C1's migration left open).
            migrationBuilder.InsertData(
                table: "RolePermissions",
                columns: new[]
                {
                    "Id", "RoleId", "PermissionId",
                    "EffectiveFromUtc", "EffectiveToUtc",
                    "CreatedBy", "CreatedUtc", "ModifiedBy", "ModifiedUtc",
                    "RowVersion", "IsDeleted",
                },
                values: new object[,]
                {
                    {
                        QualityManagerAssignReviewersRolePermissionId,
                        QualityManagerRoleId,
                        DocumentAssignReviewersPermissionId,
                        runUtc, (DateTime?)null,
                        seedBy, runUtc, seedBy, runUtc,
                        seedRowVersion, false,
                    },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Symmetric with Up. Delete the link rows first
            // (no real FK dependency at the DB level — Role and
            // Permission rows aren't FK-referenced from RolePermission
            // here, see AddPermissionsAndLinkTables — but the
            // semantic dependency order is link rows then role row).

            // Step 1 — the new QualityManager link row.
            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: QualityManagerAssignReviewersRolePermissionId);

            // Step 2 — the five DocumentAuthor link rows.
            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumn: "Id",
                keyValues: new object[]
                {
                    DocumentAuthorCreateRolePermissionId,
                    DocumentAuthorEditDraftRolePermissionId,
                    DocumentAuthorSubmitForReviewRolePermissionId,
                    DocumentAuthorAssignReviewersRolePermissionId,
                    DocumentAuthorHardDeleteRolePermissionId,
                });

            // Step 3 — the DocumentAuthor role row itself.
            migrationBuilder.DeleteData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: DocumentAuthorRoleId);
        }
    }
}
