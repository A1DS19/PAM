using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pam.Ingest.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialIngest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ingest");

            migrationBuilder.CreateTable(
                name: "vendor_transactions",
                schema: "ingest",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    vendor_id = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    vendor_reference = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    brand_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    player_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    amount_cents = table.Column<long>(type: "bigint", nullable: false),
                    currency = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    round_id = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    description = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    rejected_reason = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by_type = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    created_by_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    last_modified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    last_modified_by_type = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    last_modified_by_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vendor_transactions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_vendor_transactions_idempotency",
                schema: "ingest",
                table: "vendor_transactions",
                columns: new[] { "vendor_id", "vendor_reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_vendor_transactions_player_timeline",
                schema: "ingest",
                table: "vendor_transactions",
                columns: new[] { "brand_id", "player_id", "occurred_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_vendor_transactions_vendor_id_occurred_at",
                schema: "ingest",
                table: "vendor_transactions",
                columns: new[] { "vendor_id", "occurred_at" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "vendor_transactions",
                schema: "ingest");
        }
    }
}
