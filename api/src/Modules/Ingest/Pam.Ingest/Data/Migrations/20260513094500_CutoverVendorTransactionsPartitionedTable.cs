using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Pam.Ingest.Data;

#nullable disable

namespace Pam.Ingest.Data.Migrations;

/// <summary>
/// Installs a guarded cutover stored procedure.
/// It does NOT execute the swap at migration time.
/// </summary>
[DbContext(typeof(IngestDbContext))]
[Migration("20260513094500_CutoverVendorTransactionsPartitionedTable")]
public partial class CutoverVendorTransactionsPartitionedTable : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE OR ALTER PROCEDURE ingest.usp_partition_cutover_vendor_transactions
            AS
            BEGIN
                SET NOCOUNT ON;
                SET XACT_ABORT ON;

                IF OBJECT_ID(N'ingest.vendor_transactions', N'U') IS NULL
                    THROW 51000, 'Cutover aborted: ingest.vendor_transactions not found.', 1;

                IF OBJECT_ID(N'ingest.vendor_transactions_p', N'U') IS NULL
                    THROW 51000, 'Cutover aborted: ingest.vendor_transactions_p not found.', 1;

                IF OBJECT_ID(N'ingest.vendor_transactions_old', N'U') IS NOT NULL
                    THROW 51000, 'Cutover aborted: ingest.vendor_transactions_old already exists.', 1;

                DECLARE @liveCount bigint;
                DECLARE @shadowCount bigint;

                SELECT @liveCount = COUNT_BIG(*) FROM ingest.vendor_transactions;
                SELECT @shadowCount = COUNT_BIG(*) FROM ingest.vendor_transactions_p;

                IF (@liveCount <> @shadowCount)
                    THROW 51000, 'Cutover aborted: row count mismatch between live and shadow tables.', 1;

                EXEC sp_rename N'ingest.vendor_transactions', N'vendor_transactions_old';
                EXEC sp_rename N'ingest.vendor_transactions_p', N'vendor_transactions';

                -- PK constraint names are schema-scoped in SQL Server (unlike
                -- indexes, which are table-scoped). After the renames above,
                -- 'pk_vendor_transactions' still lives on vendor_transactions_old
                -- and 'pk_vendor_transactions_p' on vendor_transactions. Renaming
                -- the _p one to drop the suffix would collide. Drop the old
                -- table's PK first — vendor_transactions_old becomes a heap for
                -- the rollback window, then gets dropped on decommission.
                IF EXISTS (
                    SELECT 1
                    FROM sys.key_constraints
                    WHERE parent_object_id = OBJECT_ID(N'ingest.vendor_transactions_old')
                        AND name = N'pk_vendor_transactions'
                )
                    ALTER TABLE ingest.vendor_transactions_old DROP CONSTRAINT pk_vendor_transactions;

                IF EXISTS (
                    SELECT 1
                    FROM sys.key_constraints
                    WHERE parent_object_id = OBJECT_ID(N'ingest.vendor_transactions')
                        AND name = N'pk_vendor_transactions_p'
                )
                    EXEC sp_rename N'ingest.vendor_transactions.pk_vendor_transactions_p', N'pk_vendor_transactions', N'OBJECT';

                IF EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE object_id = OBJECT_ID(N'ingest.vendor_transactions')
                        AND name = N'cix_vendor_transactions_p_received_at_id'
                )
                    EXEC sp_rename N'ingest.vendor_transactions.cix_vendor_transactions_p_received_at_id', N'cix_vendor_transactions_received_at_id', N'INDEX';

                IF EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE object_id = OBJECT_ID(N'ingest.vendor_transactions')
                        AND name = N'ix_vendor_transactions_p_idempotency'
                )
                    EXEC sp_rename N'ingest.vendor_transactions.ix_vendor_transactions_p_idempotency', N'ix_vendor_transactions_idempotency', N'INDEX';

                IF EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE object_id = OBJECT_ID(N'ingest.vendor_transactions')
                        AND name = N'ix_vendor_transactions_p_player_timeline'
                )
                    EXEC sp_rename N'ingest.vendor_transactions.ix_vendor_transactions_p_player_timeline', N'ix_vendor_transactions_player_timeline', N'INDEX';

                IF EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE object_id = OBJECT_ID(N'ingest.vendor_transactions')
                        AND name = N'ix_vendor_transactions_p_vendor_id_occurred_at'
                )
                    EXEC sp_rename N'ingest.vendor_transactions.ix_vendor_transactions_p_vendor_id_occurred_at', N'ix_vendor_transactions_vendor_id_occurred_at', N'INDEX';

                IF EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE object_id = OBJECT_ID(N'ingest.vendor_transactions')
                        AND name = N'ix_vendor_transactions_p_received_at_status'
                )
                    EXEC sp_rename N'ingest.vendor_transactions.ix_vendor_transactions_p_received_at_status', N'ix_vendor_transactions_received_at_status', N'INDEX';
            END
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'ingest.usp_partition_cutover_vendor_transactions', N'P') IS NOT NULL
                DROP PROCEDURE ingest.usp_partition_cutover_vendor_transactions;
            """
        );
    }
}
