using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pam.Wallet.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialWallet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "wallet");

            migrationBuilder.CreateTable(
                name: "accounts",
                schema: "wallet",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    brand_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    player_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    currency = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by_type = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    created_by_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    last_modified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    last_modified_by_type = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    last_modified_by_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_accounts", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_accounts_brand_id",
                schema: "wallet",
                table: "accounts",
                column: "brand_id");

            migrationBuilder.CreateIndex(
                name: "ix_accounts_brand_id_player_id_currency",
                schema: "wallet",
                table: "accounts",
                columns: new[] { "brand_id", "player_id", "currency" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "accounts",
                schema: "wallet");
        }
    }
}
