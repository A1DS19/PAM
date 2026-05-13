using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Pam.Ingest.Data;

#nullable disable

namespace Pam.Ingest.Data.Migrations;

/// <summary>
/// Prepares SQL Server partitioning primitives for ingest.vendor_transactions
/// and creates a partitioned shadow table for controlled cutover.
///
/// Why shadow-table first:
/// - avoids long blocking in-place index surgery on a hot table
/// - lets us backfill by month and validate plans before swap
/// - keeps rollback simple (no destructive rewrite of live table)
/// </summary>
[DbContext(typeof(IngestDbContext))]
[Migration("20260513093000_PrepareVendorTransactionsPartitioning")]
public partial class PrepareVendorTransactionsPartitioning : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            -- Weekly partition boundaries on RANGE RIGHT, ISO-8601 weeks starting
            -- Monday 00:00 UTC. First boundary 2026-01-05 (first Monday of 2026);
            -- horizon ~10 years -> ~522 boundaries (SQL Server's 15k cap is far off).
            -- usp_partition_maintain_vendor_transactions extends the horizon daily
            -- so we never silently write to an unbounded terminal partition.
            IF NOT EXISTS (
                SELECT 1
                FROM sys.partition_functions
                WHERE name = N'pf_ingest_vendor_transactions_received_at_weekly'
            )
            BEGIN
                DECLARE @week date = '2026-01-05';
                DECLARE @end  date = '2036-01-06';
                DECLARE @values nvarchar(max) = N'';

                WHILE (@week <= @end)
                BEGIN
                    SET @values += CASE WHEN LEN(@values) = 0 THEN N'' ELSE N', ' END
                        + N'''' + CONVERT(varchar(10), @week, 23) + N'''';
                    SET @week = DATEADD(DAY, 7, @week);
                END

                DECLARE @sql nvarchar(max) =
                    N'CREATE PARTITION FUNCTION pf_ingest_vendor_transactions_received_at_weekly '
                    + N'(datetimeoffset(7)) AS RANGE RIGHT FOR VALUES (' + @values + N');';
                EXEC(@sql);
            END
            """
        );

        migrationBuilder.Sql(
            """
            IF NOT EXISTS (
                SELECT 1
                FROM sys.partition_schemes
                WHERE name = N'ps_ingest_vendor_transactions_received_at_weekly'
            )
            BEGIN
                CREATE PARTITION SCHEME ps_ingest_vendor_transactions_received_at_weekly
                AS PARTITION pf_ingest_vendor_transactions_received_at_weekly
                ALL TO ([PRIMARY]);
            END
            """
        );

        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'ingest.vendor_transactions_p', N'U') IS NULL
            BEGIN
                CREATE TABLE ingest.vendor_transactions_p
                (
                    id uniqueidentifier NOT NULL,
                    vendor_id nvarchar(32) NOT NULL,
                    vendor_reference nvarchar(400) NOT NULL,
                    brand_id uniqueidentifier NOT NULL,
                    player_id uniqueidentifier NOT NULL,
                    amount_cents bigint NOT NULL,
                    currency nchar(3) NOT NULL,
                    kind nvarchar(16) NOT NULL,
                    status nvarchar(16) NOT NULL,
                    round_id nvarchar(200) NULL,
                    description nvarchar(250) NULL,
                    occurred_at datetimeoffset NOT NULL,
                    received_at datetimeoffset NOT NULL,
                    rejected_reason nvarchar(64) NULL,
                    created_at datetimeoffset NOT NULL,
                    created_by_type nvarchar(16) NOT NULL,
                    created_by_id nvarchar(128) NOT NULL,
                    last_modified_at datetimeoffset NULL,
                    last_modified_by_type nvarchar(16) NULL,
                    last_modified_by_id nvarchar(128) NULL,
                    CONSTRAINT pk_vendor_transactions_p PRIMARY KEY NONCLUSTERED (id) ON [PRIMARY]
                )
                ON ps_ingest_vendor_transactions_received_at_weekly(received_at);

                -- Partition-aligned indexes use STATISTICS_INCREMENTAL = ON so
                -- usp_partition_maintain_vendor_transactions can UPDATE STATISTICS
                -- on the hot partition only — O(hot-rows) instead of O(table).
                -- ix_..._idempotency stays non-aligned (global UNIQUE on
                -- vendor_id+vendor_reference). Side effect: SWITCH PARTITION will
                -- not work on this table until the idempotency-index strategy is
                -- redesigned. See docs/DB_SCALING.md "Archival decision pending".
                CREATE CLUSTERED INDEX cix_vendor_transactions_p_received_at_id
                    ON ingest.vendor_transactions_p(received_at, id)
                    WITH (STATISTICS_INCREMENTAL = ON)
                    ON ps_ingest_vendor_transactions_received_at_weekly(received_at);

                CREATE UNIQUE NONCLUSTERED INDEX ix_vendor_transactions_p_idempotency
                    ON ingest.vendor_transactions_p(vendor_id, vendor_reference)
                    ON [PRIMARY];

                CREATE NONCLUSTERED INDEX ix_vendor_transactions_p_player_timeline
                    ON ingest.vendor_transactions_p(brand_id, player_id, occurred_at DESC)
                    WITH (STATISTICS_INCREMENTAL = ON)
                    ON ps_ingest_vendor_transactions_received_at_weekly(received_at);

                CREATE NONCLUSTERED INDEX ix_vendor_transactions_p_vendor_id_occurred_at
                    ON ingest.vendor_transactions_p(vendor_id, occurred_at DESC)
                    WITH (STATISTICS_INCREMENTAL = ON)
                    ON ps_ingest_vendor_transactions_received_at_weekly(received_at);

                CREATE NONCLUSTERED INDEX ix_vendor_transactions_p_received_at_status
                    ON ingest.vendor_transactions_p(received_at, status)
                    WITH (STATISTICS_INCREMENTAL = ON)
                    ON ps_ingest_vendor_transactions_received_at_weekly(received_at);
            END
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'ingest.vendor_transactions_p', N'U') IS NOT NULL
            BEGIN
                DROP TABLE ingest.vendor_transactions_p;
            END
            """
        );

        migrationBuilder.Sql(
            """
            IF EXISTS (
                SELECT 1
                FROM sys.partition_schemes
                WHERE name = N'ps_ingest_vendor_transactions_received_at_weekly'
            )
            BEGIN
                DROP PARTITION SCHEME ps_ingest_vendor_transactions_received_at_weekly;
            END
            """
        );

        migrationBuilder.Sql(
            """
            IF EXISTS (
                SELECT 1
                FROM sys.partition_functions
                WHERE name = N'pf_ingest_vendor_transactions_received_at_weekly'
            )
            BEGIN
                DROP PARTITION FUNCTION pf_ingest_vendor_transactions_received_at_weekly;
            END
            """
        );
    }
}
