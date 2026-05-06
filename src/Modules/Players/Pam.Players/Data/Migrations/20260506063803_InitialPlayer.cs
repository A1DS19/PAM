using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pam.Players.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialPlayer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "player");

            migrationBuilder.CreateTable(
                name: "players",
                schema: "player",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    brand_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    identity_provider_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    first_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    last_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    middle_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    date_of_birth = table.Column<DateOnly>(type: "date", nullable: false),
                    country_code = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    region = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    email_verified = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_by_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    last_modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_modified_by_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    last_modified_by_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_players", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_players_brand_id",
                schema: "player",
                table: "players",
                column: "brand_id");

            migrationBuilder.CreateIndex(
                name: "IX_players_identity_provider_id",
                schema: "player",
                table: "players",
                column: "identity_provider_id",
                unique: true);

            // Per-brand email uniqueness, enforced at the database level.
            // EF Core's lambda-based index API does not support property
            // paths through owned-type navigations, so this is raw SQL.
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX \"IX_players_brand_id_email\" ON player.players (brand_id, email);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "players",
                schema: "player");
        }
    }
}
