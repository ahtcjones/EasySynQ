using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasySynQ.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPermissionsAndLinkTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Permissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "BLOB", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Permissions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RolePermissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RoleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PermissionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EffectiveFromUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EffectiveToUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "BLOB", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RolePermissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RolePermissions_Permissions_PermissionId",
                        column: x => x.PermissionId,
                        principalTable: "Permissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RolePermissions_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserPermissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PermissionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EffectiveFromUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EffectiveToUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "BLOB", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPermissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserPermissions_Permissions_PermissionId",
                        column: x => x.PermissionId,
                        principalTable: "Permissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserPermissions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Permissions_Name",
                table: "Permissions",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissions_EffectiveFromUtc",
                table: "RolePermissions",
                column: "EffectiveFromUtc");

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissions_EffectiveToUtc",
                table: "RolePermissions",
                column: "EffectiveToUtc");

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissions_PermissionId",
                table: "RolePermissions",
                column: "PermissionId");

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissions_RoleId_PermissionId",
                table: "RolePermissions",
                columns: new[] { "RoleId", "PermissionId" });

            migrationBuilder.CreateIndex(
                name: "IX_UserPermissions_EffectiveFromUtc",
                table: "UserPermissions",
                column: "EffectiveFromUtc");

            migrationBuilder.CreateIndex(
                name: "IX_UserPermissions_EffectiveToUtc",
                table: "UserPermissions",
                column: "EffectiveToUtc");

            migrationBuilder.CreateIndex(
                name: "IX_UserPermissions_PermissionId",
                table: "UserPermissions",
                column: "PermissionId");

            migrationBuilder.CreateIndex(
                name: "IX_UserPermissions_UserId_PermissionId",
                table: "UserPermissions",
                columns: new[] { "UserId", "PermissionId" });

            // ADR 0007 §Decision — seed the Phase 1 system permissions.
            // Deterministic Ids (prefix 07000000- marks the ADR origin)
            // so every install across every environment gets the same
            // row identifiers — auditable rows reference these by Id.
            // Seed timestamp is the ADR landing date (2026-05-13 UTC).
            // CreatedBy/ModifiedBy = "system:migration" attributes the
            // row to its migration source rather than a real user (the
            // interceptor cannot run for migration-time inserts; this
            // sentinel makes the source unambiguous in the audit log).
            // RowVersion is the all-zeros sentinel; the interceptor
            // assigns a real value on first EF-tracked modification.
            var seedUtc = new DateTime(2026, 5, 13, 0, 0, 0, DateTimeKind.Utc);
            var seedRowVersion = new byte[8];
            const string seedBy = "system:migration";
            const string systemCategory = "System";

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
                        new Guid("07000000-0000-0000-0000-000000000001"),
                        "System.Administer",
                        "Top-level system administration capability.",
                        systemCategory,
                        seedBy, seedUtc, seedBy, seedUtc,
                        seedRowVersion, false,
                    },
                    {
                        new Guid("07000000-0000-0000-0000-000000000002"),
                        "Role.Create",
                        "Create new role definitions.",
                        systemCategory,
                        seedBy, seedUtc, seedBy, seedUtc,
                        seedRowVersion, false,
                    },
                    {
                        new Guid("07000000-0000-0000-0000-000000000003"),
                        "Role.Edit",
                        "Edit existing role names or descriptions.",
                        systemCategory,
                        seedBy, seedUtc, seedBy, seedUtc,
                        seedRowVersion, false,
                    },
                    {
                        new Guid("07000000-0000-0000-0000-000000000004"),
                        "Role.Delete",
                        "Soft-delete role definitions.",
                        systemCategory,
                        seedBy, seedUtc, seedBy, seedUtc,
                        seedRowVersion, false,
                    },
                    {
                        new Guid("07000000-0000-0000-0000-000000000005"),
                        "Role.AssignPermissions",
                        "Attach permissions to or detach permissions from a role.",
                        systemCategory,
                        seedBy, seedUtc, seedBy, seedUtc,
                        seedRowVersion, false,
                    },
                    {
                        new Guid("07000000-0000-0000-0000-000000000006"),
                        "User.Create",
                        "Create new user accounts.",
                        systemCategory,
                        seedBy, seedUtc, seedBy, seedUtc,
                        seedRowVersion, false,
                    },
                    {
                        new Guid("07000000-0000-0000-0000-000000000007"),
                        "User.Edit",
                        "Edit user-account fields (display name, username, etc.).",
                        systemCategory,
                        seedBy, seedUtc, seedBy, seedUtc,
                        seedRowVersion, false,
                    },
                    {
                        new Guid("07000000-0000-0000-0000-000000000008"),
                        "User.Disable",
                        "Administratively disable a user account.",
                        systemCategory,
                        seedBy, seedUtc, seedBy, seedUtc,
                        seedRowVersion, false,
                    },
                    {
                        new Guid("07000000-0000-0000-0000-000000000009"),
                        "User.AssignRoles",
                        "Attach roles to or detach roles from a user.",
                        systemCategory,
                        seedBy, seedUtc, seedBy, seedUtc,
                        seedRowVersion, false,
                    },
                    {
                        new Guid("07000000-0000-0000-0000-00000000000a"),
                        "User.GrantPermissions",
                        "Grant permissions directly to a user, bypassing role membership.",
                        systemCategory,
                        seedBy, seedUtc, seedBy, seedUtc,
                        seedRowVersion, false,
                    },
                    {
                        new Guid("07000000-0000-0000-0000-00000000000b"),
                        "AuditLog.Read",
                        "Read the append-only audit log.",
                        systemCategory,
                        seedBy, seedUtc, seedBy, seedUtc,
                        seedRowVersion, false,
                    },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RolePermissions");

            migrationBuilder.DropTable(
                name: "UserPermissions");

            migrationBuilder.DropTable(
                name: "Permissions");
        }
    }
}
