using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pam.Players.Data.Migrations
{
    /// <inheritdoc />
    public partial class TypedActorAuditColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "created_by",
                schema: "player",
                table: "players");

            migrationBuilder.RenameColumn(
                name: "last_modified_by",
                schema: "player",
                table: "players",
                newName: "last_modified_by_id");

            // Existing dev rows are stamped with the System actor — there is
            // no way to recover the prior creator type from the dropped
            // created_by string column.
            migrationBuilder.AddColumn<string>(
                name: "created_by_id",
                schema: "player",
                table: "players",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "system");

            migrationBuilder.AddColumn<string>(
                name: "created_by_type",
                schema: "player",
                table: "players",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "System");

            migrationBuilder.AddColumn<string>(
                name: "last_modified_by_type",
                schema: "player",
                table: "players",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "created_by_id",
                schema: "player",
                table: "players");

            migrationBuilder.DropColumn(
                name: "created_by_type",
                schema: "player",
                table: "players");

            migrationBuilder.DropColumn(
                name: "last_modified_by_type",
                schema: "player",
                table: "players");

            migrationBuilder.RenameColumn(
                name: "last_modified_by_id",
                schema: "player",
                table: "players",
                newName: "last_modified_by");

            migrationBuilder.AddColumn<string>(
                name: "created_by",
                schema: "player",
                table: "players",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }
    }
}
