using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pam.Audit.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.CreateTable(
                name: "command_log",
                schema: "audit",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    correlation_id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    actor_type = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    actor_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    request_type = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    payload_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    duration_ms = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    error_type = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    error_message = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_command_log", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_command_log_actor_id_started_at",
                schema: "audit",
                table: "command_log",
                columns: new[] { "actor_id", "started_at" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "command_log",
                schema: "audit");
        }
    }
}
