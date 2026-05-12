using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pam.Players.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialPlayers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "players");

            migrationBuilder.CreateTable(
                name: "players",
                schema: "players",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    brand_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by_type = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    created_by_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    last_modified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    last_modified_by_type = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    last_modified_by_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_players", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_players_brand_id",
                schema: "players",
                table: "players",
                column: "brand_id");

            migrationBuilder.CreateIndex(
                name: "ix_players_brand_id_email",
                schema: "players",
                table: "players",
                columns: new[] { "brand_id", "email" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "players",
                schema: "players");
        }
    }
}
