using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Pam.Ingest.Data;

#nullable disable

namespace Pam.Ingest.Data.Migrations;

/// <summary>
/// Installs the partition lifecycle procedure for ingest.vendor_transactions.
/// Scheduling is handled in C# by PartitionMaintenanceService — daily tick
/// driven by the API process, no SQL Agent dependency.
///
/// The SP is idempotent and safe to run at any time:
///   - SPLITs future weekly boundaries to keep ≥ @future_weeks_to_keep ahead
///     of GETUTCDATE(). Defensive against the 10-year preallocation drying up.
///   - UPDATE STATISTICS WITH RESAMPLE ON PARTITIONS (hot partition only),
///     cheap thanks to STATISTICS_INCREMENTAL = ON on the aligned indexes.
/// </summary>
[DbContext(typeof(IngestDbContext))]
[Migration("20260513120000_AddPartitionMaintenanceProcedureAndSqlAgentJob")]
public partial class AddPartitionMaintenanceProcedureAndSqlAgentJob : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE OR ALTER PROCEDURE ingest.usp_partition_maintain_vendor_transactions
                @future_weeks_to_keep int = 12
            AS
            BEGIN
                SET NOCOUNT ON;
                SET XACT_ABORT ON;

                IF @future_weeks_to_keep < 1 OR @future_weeks_to_keep > 520
                    THROW 51000, 'usp_partition_maintain_vendor_transactions: @future_weeks_to_keep must be between 1 and 520.', 1;

                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.partition_functions
                    WHERE name = N'pf_ingest_vendor_transactions_received_at_weekly'
                )
                BEGIN
                    PRINT 'usp_partition_maintain_vendor_transactions: pf_ingest_vendor_transactions_received_at_weekly not found; skipping.';
                    RETURN;
                END

                DECLARE @lastBoundary datetimeoffset(7);
                DECLARE @targetBoundary datetimeoffset(7);
                DECLARE @nextBoundary datetimeoffset(7);
                DECLARE @splitCount int = 0;

                SELECT TOP 1 @lastBoundary = CAST(prv.value AS datetimeoffset(7))
                FROM sys.partition_range_values prv
                JOIN sys.partition_functions pf ON pf.function_id = prv.function_id
                WHERE pf.name = N'pf_ingest_vendor_transactions_received_at_weekly'
                ORDER BY prv.boundary_id DESC;

                IF @lastBoundary IS NULL
                BEGIN
                    PRINT 'usp_partition_maintain_vendor_transactions: partition function has no boundaries; skipping.';
                    RETURN;
                END

                SET @targetBoundary = CAST(
                    DATEADD(WEEK, @future_weeks_to_keep, CAST(SYSUTCDATETIME() AS date))
                    AS datetimeoffset(7));

                WHILE (@lastBoundary < @targetBoundary)
                BEGIN
                    SET @nextBoundary = DATEADD(DAY, 7, @lastBoundary);

                    ALTER PARTITION SCHEME ps_ingest_vendor_transactions_received_at_weekly
                        NEXT USED [PRIMARY];

                    ALTER PARTITION FUNCTION pf_ingest_vendor_transactions_received_at_weekly()
                        SPLIT RANGE (@nextBoundary);

                    SET @lastBoundary = @nextBoundary;
                    SET @splitCount += 1;
                END

                -- Stats refresh on the hot partition only. Skipped pre-cutover
                -- (live vendor_transactions is still unpartitioned then).
                IF OBJECT_ID(N'ingest.vendor_transactions', N'U') IS NOT NULL
                BEGIN
                    DECLARE @isPartitioned bit = 0;
                    SELECT @isPartitioned = CASE WHEN COUNT(*) > 1 THEN 1 ELSE 0 END
                    FROM sys.partitions
                    WHERE object_id = OBJECT_ID(N'ingest.vendor_transactions')
                      AND index_id IN (0, 1);

                    IF (@isPartitioned = 1)
                    BEGIN
                        DECLARE @hotPartition int =
                            $PARTITION.pf_ingest_vendor_transactions_received_at_weekly(
                                CAST(SYSUTCDATETIME() AS datetimeoffset(7)));

                        DECLARE @stmt nvarchar(max) =
                            N'UPDATE STATISTICS ingest.vendor_transactions '
                            + N'WITH RESAMPLE, ON PARTITIONS (' + CAST(@hotPartition AS nvarchar(10)) + N');';
                        EXEC sp_executesql @stmt;
                    END
                END

                PRINT CONCAT(
                    'usp_partition_maintain_vendor_transactions: split ',
                    @splitCount,
                    ' new weekly boundaries. Last boundary now: ',
                    CONVERT(varchar(33), @lastBoundary, 126), '.');
            END
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'ingest.usp_partition_maintain_vendor_transactions', N'P') IS NOT NULL
                DROP PROCEDURE ingest.usp_partition_maintain_vendor_transactions;
            """
        );
    }
}
