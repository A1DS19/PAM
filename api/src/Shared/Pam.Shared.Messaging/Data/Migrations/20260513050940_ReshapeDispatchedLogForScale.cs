using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pam.Shared.Messaging.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReshapeDispatchedLogForScale : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_outbox_dispatched_log",
                schema: "messaging",
                table: "outbox_dispatched_log");

            migrationBuilder.AddPrimaryKey(
                name: "pk_outbox_dispatched_log",
                schema: "messaging",
                table: "outbox_dispatched_log",
                columns: new[] { "module", "event_type", "business_pk" });

            migrationBuilder.CreateIndex(
                name: "IX_outbox_dispatched_log_dispatched_at",
                schema: "messaging",
                table: "outbox_dispatched_log",
                column: "dispatched_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_outbox_dispatched_log",
                schema: "messaging",
                table: "outbox_dispatched_log");

            migrationBuilder.DropIndex(
                name: "IX_outbox_dispatched_log_dispatched_at",
                schema: "messaging",
                table: "outbox_dispatched_log");

            migrationBuilder.AddPrimaryKey(
                name: "pk_outbox_dispatched_log",
                schema: "messaging",
                table: "outbox_dispatched_log",
                columns: new[] { "module", "business_pk", "event_type" });
        }
    }
}
