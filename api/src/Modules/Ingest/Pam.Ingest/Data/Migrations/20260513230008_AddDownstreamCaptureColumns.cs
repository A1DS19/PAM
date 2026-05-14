using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pam.Ingest.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDownstreamCaptureColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "downstream_latency_ms",
                schema: "ingest",
                table: "vendor_transactions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "downstream_outcome_code",
                schema: "ingest",
                table: "vendor_transactions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "downstream_outcome_message",
                schema: "ingest",
                table: "vendor_transactions",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "downstream_reference",
                schema: "ingest",
                table: "vendor_transactions",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "downstream_status",
                schema: "ingest",
                table: "vendor_transactions",
                type: "nvarchar(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "NotApplicable");

            migrationBuilder.AddColumn<long>(
                name: "vendor_balance_after_cents",
                schema: "ingest",
                table: "vendor_transactions",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "downstream_latency_ms",
                schema: "ingest",
                table: "vendor_transactions");

            migrationBuilder.DropColumn(
                name: "downstream_outcome_code",
                schema: "ingest",
                table: "vendor_transactions");

            migrationBuilder.DropColumn(
                name: "downstream_outcome_message",
                schema: "ingest",
                table: "vendor_transactions");

            migrationBuilder.DropColumn(
                name: "downstream_reference",
                schema: "ingest",
                table: "vendor_transactions");

            migrationBuilder.DropColumn(
                name: "downstream_status",
                schema: "ingest",
                table: "vendor_transactions");

            migrationBuilder.DropColumn(
                name: "vendor_balance_after_cents",
                schema: "ingest",
                table: "vendor_transactions");
        }
    }
}
