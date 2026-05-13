using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pam.Shared.Messaging.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboxDispatchedLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "outbox_dispatched_log",
                schema: "messaging",
                columns: table => new
                {
                    module = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    business_pk = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    event_type = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    dispatched_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_dispatched_log", x => new { x.module, x.business_pk, x.event_type });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outbox_dispatched_log",
                schema: "messaging");
        }
    }
}
