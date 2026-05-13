using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pam.Ingest.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReceivedAtStatusIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_vendor_transactions_received_at_status",
                schema: "ingest",
                table: "vendor_transactions",
                columns: new[] { "received_at", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_vendor_transactions_received_at_status",
                schema: "ingest",
                table: "vendor_transactions");
        }
    }
}
