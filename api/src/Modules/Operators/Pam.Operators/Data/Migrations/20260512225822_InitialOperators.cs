using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pam.Operators.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialOperators : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "operators");

            migrationBuilder.CreateTable(
                name: "brands",
                schema: "operators",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    slug = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    jurisdiction = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by_type = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    created_by_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    last_modified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    last_modified_by_type = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    last_modified_by_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_brands", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_brands_slug",
                schema: "operators",
                table: "brands",
                column: "slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "brands",
                schema: "operators");
        }
    }
}
