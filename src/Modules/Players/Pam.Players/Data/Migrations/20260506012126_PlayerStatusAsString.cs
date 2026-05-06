using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pam.Players.Data.Migrations
{
    /// <inheritdoc />
    public partial class PlayerStatusAsString : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // PostgreSQL does not auto-cast int → varchar. Map every existing
            // ordinal to its enum name in one ALTER ... USING expression so
            // the conversion is in-place and atomic.
            migrationBuilder.Sql(
                """
                ALTER TABLE player.players
                ALTER COLUMN status TYPE varchar(16)
                USING CASE status
                    WHEN 0 THEN 'Pending'
                    WHEN 1 THEN 'Active'
                    WHEN 2 THEN 'Suspended'
                    WHEN 3 THEN 'Closed'
                    ELSE 'Pending'
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE player.players
                ALTER COLUMN status TYPE integer
                USING CASE status
                    WHEN 'Pending' THEN 0
                    WHEN 'Active' THEN 1
                    WHEN 'Suspended' THEN 2
                    WHEN 'Closed' THEN 3
                    ELSE 0
                END;
                """);
        }
    }
}
