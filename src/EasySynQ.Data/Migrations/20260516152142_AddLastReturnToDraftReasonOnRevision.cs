using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasySynQ.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLastReturnToDraftReasonOnRevision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LastReturnToDraftReason",
                table: "DocumentRevisions",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastReturnToDraftReason",
                table: "DocumentRevisions");
        }
    }
}
