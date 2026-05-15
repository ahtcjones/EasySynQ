using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasySynQ.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentControllerTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Number = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    RetiredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RetiredByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RetirementSignatureId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "BLOB", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Documents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExternalDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    IssuingBody = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Designation = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    CurrentRevisionLabel = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CurrentEffectiveDateUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "BLOB", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalDocuments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VaultBlobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sha256Hash = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 64, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    MimeType = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    OriginalFileName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    StoredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "BLOB", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VaultBlobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DocumentRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DocumentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RevisionLabel = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Lifecycle = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    EffectiveFromUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    VaultBlobId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AuthorUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AuthorSignatureId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "BLOB", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    LockedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentRevisions_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentRevisions_VaultBlobs_VaultBlobId",
                        column: x => x.VaultBlobId,
                        principalTable: "VaultBlobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DocumentLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DocumentRevisionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExternalDocumentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompatibilityReviewRequiredFlag = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "BLOB", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentLinks_DocumentRevisions_DocumentRevisionId",
                        column: x => x.DocumentRevisionId,
                        principalTable: "DocumentRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentLinks_ExternalDocuments_ExternalDocumentId",
                        column: x => x.ExternalDocumentId,
                        principalTable: "ExternalDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DocumentReviewAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DocumentRevisionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReviewerUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AssignedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AssignedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    SignedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SignatureId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "BLOB", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentReviewAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentReviewAssignments_DocumentRevisions_DocumentRevisionId",
                        column: x => x.DocumentRevisionId,
                        principalTable: "DocumentRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DocumentReviewComments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DocumentRevisionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BodyText = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "BLOB", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentReviewComments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentReviewComments_DocumentRevisions_DocumentRevisionId",
                        column: x => x.DocumentRevisionId,
                        principalTable: "DocumentRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentLinks_DocumentRevisionId",
                table: "DocumentLinks",
                column: "DocumentRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentLinks_ExternalDocumentId",
                table: "DocumentLinks",
                column: "ExternalDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentReviewAssignments_DocumentRevisionId",
                table: "DocumentReviewAssignments",
                column: "DocumentRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentReviewComments_DocumentRevisionId",
                table: "DocumentReviewComments",
                column: "DocumentRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentRevisions_DocumentId",
                table: "DocumentRevisions",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentRevisions_EffectiveFromUtc",
                table: "DocumentRevisions",
                column: "EffectiveFromUtc");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentRevisions_Lifecycle",
                table: "DocumentRevisions",
                column: "Lifecycle");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentRevisions_VaultBlobId",
                table: "DocumentRevisions",
                column: "VaultBlobId");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_Number",
                table: "Documents",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExternalDocuments_IssuingBody_Designation",
                table: "ExternalDocuments",
                columns: new[] { "IssuingBody", "Designation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VaultBlobs_Sha256Hash",
                table: "VaultBlobs",
                column: "Sha256Hash",
                unique: true);

            // ADR 0008 §Document permissions catalog — seed the 13
            // Phase 2 permissions. Deterministic Ids (prefix
            // 08000000-) parallel ADR 0007's 07-prefix convention so
            // audit-log rows that reference PermissionId Guids remain
            // stable across environments. Seed timestamp is the ADR
            // landing date (2026-05-14 UTC).
            // CreatedBy/ModifiedBy = "system:migration" attributes the
            // row to its migration source rather than a real user (the
            // interceptor cannot run for migration-time inserts).
            // RowVersion is the all-zeros sentinel; the interceptor
            // assigns a real value on first EF-tracked modification.
            var seedUtc = new DateTime(2026, 5, 14, 0, 0, 0, DateTimeKind.Utc);
            var seedRowVersion = new byte[8];
            const string seedBy = "system:migration";
            const string documentCategory = "Document";
            const string externalDocumentCategory = "ExternalDocument";
            const string documentLinkCategory = "DocumentLink";

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
                        new Guid("08000000-0000-0000-0000-000000000001"),
                        "Document.Create",
                        "Create a new internal document (initial Draft revision).",
                        documentCategory,
                        seedBy, seedUtc, seedBy, seedUtc,
                        seedRowVersion, false,
                    },
                    {
                        new Guid("08000000-0000-0000-0000-000000000002"),
                        "Document.EditDraft",
                        "Edit a document while it is in Draft state.",
                        documentCategory,
                        seedBy, seedUtc, seedBy, seedUtc,
                        seedRowVersion, false,
                    },
                    {
                        new Guid("08000000-0000-0000-0000-000000000003"),
                        "Document.SubmitForReview",
                        "Move a Draft revision to In Review.",
                        documentCategory,
                        seedBy, seedUtc, seedBy, seedUtc,
                        seedRowVersion, false,
                    },
                    {
                        new Guid("08000000-0000-0000-0000-000000000004"),
                        "Document.AssignReviewers",
                        "Name the reviewer set when submitting for review. Intentionally not assigned to the seeded QualityManager role by default per ADR 0008 — granted by organizations either to author roles (small-shop default) or restricted to QM (strict-gatekeeper policy).",
                        documentCategory,
                        seedBy, seedUtc, seedBy, seedUtc,
                        seedRowVersion, false,
                    },
                    {
                        new Guid("08000000-0000-0000-0000-000000000005"),
                        "Document.Review",
                        "Sign as a reviewer on a document where the user is in the assigned-reviewers list.",
                        documentCategory,
                        seedBy, seedUtc, seedBy, seedUtc,
                        seedRowVersion, false,
                    },
                    {
                        new Guid("08000000-0000-0000-0000-000000000006"),
                        "Document.ReturnForEdits",
                        "Return an In Review document to Draft for author edits.",
                        documentCategory,
                        seedBy, seedUtc, seedBy, seedUtc,
                        seedRowVersion, false,
                    },
                    {
                        new Guid("08000000-0000-0000-0000-000000000007"),
                        "Document.Retire",
                        "Retire an Active document (no new revision).",
                        documentCategory,
                        seedBy, seedUtc, seedBy, seedUtc,
                        seedRowVersion, false,
                    },
                    {
                        new Guid("08000000-0000-0000-0000-000000000008"),
                        "Document.SoftDelete",
                        "Soft-delete a document or revision (with reason) once it is past the hard-delete boundary.",
                        documentCategory,
                        seedBy, seedUtc, seedBy, seedUtc,
                        seedRowVersion, false,
                    },
                    {
                        new Guid("08000000-0000-0000-0000-000000000009"),
                        "Document.HardDelete",
                        "Hard-delete a Draft revision that has no signatures and is not referenced by any signed record.",
                        documentCategory,
                        seedBy, seedUtc, seedBy, seedUtc,
                        seedRowVersion, false,
                    },
                    {
                        new Guid("08000000-0000-0000-0000-00000000000a"),
                        "Document.ViewArchived",
                        "View Superseded and Archived revisions in the UI (not in the default Active-only list).",
                        documentCategory,
                        seedBy, seedUtc, seedBy, seedUtc,
                        seedRowVersion, false,
                    },
                    {
                        new Guid("08000000-0000-0000-0000-00000000000b"),
                        "ExternalDocument.Create",
                        "Add a new External document reference (ASTM, AMS, customer spec).",
                        externalDocumentCategory,
                        seedBy, seedUtc, seedBy, seedUtc,
                        seedRowVersion, false,
                    },
                    {
                        new Guid("08000000-0000-0000-0000-00000000000c"),
                        "ExternalDocument.UpdateRevision",
                        "Record a new revision of an External document.",
                        externalDocumentCategory,
                        seedBy, seedUtc, seedBy, seedUtc,
                        seedRowVersion, false,
                    },
                    {
                        new Guid("08000000-0000-0000-0000-00000000000d"),
                        "DocumentLink.Manage",
                        "Create or remove links between internal documents and external documents.",
                        documentLinkCategory,
                        seedBy, seedUtc, seedBy, seedUtc,
                        seedRowVersion, false,
                    },
                });

            // ADR 0008 §Seeded QualityManager role — seed the role row
            // with its full operational document permission set
            // (everything in Phase2Document except Document.AssignReviewers).
            // The role Id is deterministic (prefix 08100000-) so the
            // dev DB and every test DB pin the same identifier;
            // RolePermission rows reference it by Id.
            var qualityManagerRoleId = new Guid("08100000-0000-0000-0000-000000000001");

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
                        qualityManagerRoleId,
                        "QualityManager",
                        "Default operational role for document-controller authority. Holds all Document and ExternalDocument permissions seeded by Phase 2's migration except Document.AssignReviewers, which organizations grant separately per their reviewer-assignment policy.",
                        seedBy, seedUtc, seedBy, seedUtc,
                        seedRowVersion, false,
                    },
                });

            // QualityManager → permissions link rows. Twelve rows —
            // every Phase 2 document permission EXCEPT
            // Document.AssignReviewers (intentional omission per ADR
            // 0008; the migration-seed test verifies the omission
            // explicitly).
            //
            // RolePermission Ids use the 08200000- prefix; the suffix
            // matches the permission's own suffix (with the 04 slot
            // skipped) for direct visual tracing between the two
            // tables in raw queries.
            //
            // EffectiveFromUtc = seed instant; EffectiveToUtc = null
            // (open-ended) — the seeded grants are always-on for the
            // role's lifetime.
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
                        new Guid("08200000-0000-0000-0000-000000000001"),
                        qualityManagerRoleId,
                        new Guid("08000000-0000-0000-0000-000000000001"), // Document.Create
                        seedUtc, (DateTime?)null,
                        seedBy, seedUtc, seedBy, seedUtc,
                        seedRowVersion, false,
                    },
                    {
                        new Guid("08200000-0000-0000-0000-000000000002"),
                        qualityManagerRoleId,
                        new Guid("08000000-0000-0000-0000-000000000002"), // Document.EditDraft
                        seedUtc, (DateTime?)null,
                        seedBy, seedUtc, seedBy, seedUtc,
                        seedRowVersion, false,
                    },
                    {
                        new Guid("08200000-0000-0000-0000-000000000003"),
                        qualityManagerRoleId,
                        new Guid("08000000-0000-0000-0000-000000000003"), // Document.SubmitForReview
                        seedUtc, (DateTime?)null,
                        seedBy, seedUtc, seedBy, seedUtc,
                        seedRowVersion, false,
                    },
                    // Slot 04 deliberately skipped — Document.AssignReviewers
                    // is not granted to QualityManager by default.
                    {
                        new Guid("08200000-0000-0000-0000-000000000005"),
                        qualityManagerRoleId,
                        new Guid("08000000-0000-0000-0000-000000000005"), // Document.Review
                        seedUtc, (DateTime?)null,
                        seedBy, seedUtc, seedBy, seedUtc,
                        seedRowVersion, false,
                    },
                    {
                        new Guid("08200000-0000-0000-0000-000000000006"),
                        qualityManagerRoleId,
                        new Guid("08000000-0000-0000-0000-000000000006"), // Document.ReturnForEdits
                        seedUtc, (DateTime?)null,
                        seedBy, seedUtc, seedBy, seedUtc,
                        seedRowVersion, false,
                    },
                    {
                        new Guid("08200000-0000-0000-0000-000000000007"),
                        qualityManagerRoleId,
                        new Guid("08000000-0000-0000-0000-000000000007"), // Document.Retire
                        seedUtc, (DateTime?)null,
                        seedBy, seedUtc, seedBy, seedUtc,
                        seedRowVersion, false,
                    },
                    {
                        new Guid("08200000-0000-0000-0000-000000000008"),
                        qualityManagerRoleId,
                        new Guid("08000000-0000-0000-0000-000000000008"), // Document.SoftDelete
                        seedUtc, (DateTime?)null,
                        seedBy, seedUtc, seedBy, seedUtc,
                        seedRowVersion, false,
                    },
                    {
                        new Guid("08200000-0000-0000-0000-000000000009"),
                        qualityManagerRoleId,
                        new Guid("08000000-0000-0000-0000-000000000009"), // Document.HardDelete
                        seedUtc, (DateTime?)null,
                        seedBy, seedUtc, seedBy, seedUtc,
                        seedRowVersion, false,
                    },
                    {
                        new Guid("08200000-0000-0000-0000-00000000000a"),
                        qualityManagerRoleId,
                        new Guid("08000000-0000-0000-0000-00000000000a"), // Document.ViewArchived
                        seedUtc, (DateTime?)null,
                        seedBy, seedUtc, seedBy, seedUtc,
                        seedRowVersion, false,
                    },
                    {
                        new Guid("08200000-0000-0000-0000-00000000000b"),
                        qualityManagerRoleId,
                        new Guid("08000000-0000-0000-0000-00000000000b"), // ExternalDocument.Create
                        seedUtc, (DateTime?)null,
                        seedBy, seedUtc, seedBy, seedUtc,
                        seedRowVersion, false,
                    },
                    {
                        new Guid("08200000-0000-0000-0000-00000000000c"),
                        qualityManagerRoleId,
                        new Guid("08000000-0000-0000-0000-00000000000c"), // ExternalDocument.UpdateRevision
                        seedUtc, (DateTime?)null,
                        seedBy, seedUtc, seedBy, seedUtc,
                        seedRowVersion, false,
                    },
                    {
                        new Guid("08200000-0000-0000-0000-00000000000d"),
                        qualityManagerRoleId,
                        new Guid("08000000-0000-0000-0000-00000000000d"), // DocumentLink.Manage
                        seedUtc, (DateTime?)null,
                        seedBy, seedUtc, seedBy, seedUtc,
                        seedRowVersion, false,
                    },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentLinks");

            migrationBuilder.DropTable(
                name: "DocumentReviewAssignments");

            migrationBuilder.DropTable(
                name: "DocumentReviewComments");

            migrationBuilder.DropTable(
                name: "ExternalDocuments");

            migrationBuilder.DropTable(
                name: "DocumentRevisions");

            migrationBuilder.DropTable(
                name: "Documents");

            migrationBuilder.DropTable(
                name: "VaultBlobs");
        }
    }
}
