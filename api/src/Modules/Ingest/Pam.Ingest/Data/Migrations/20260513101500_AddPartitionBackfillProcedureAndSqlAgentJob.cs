using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Pam.Ingest.Data;

#nullable disable

namespace Pam.Ingest.Data.Migrations;

/// <summary>
/// Adds a resumable in-DB backfill procedure and, when SQL Server Agent is
/// available, provisions a recurring Agent job to run it.
/// </summary>
[DbContext(typeof(IngestDbContext))]
[Migration("20260513101500_AddPartitionBackfillProcedureAndSqlAgentJob")]
public partial class AddPartitionBackfillProcedureAndSqlAgentJob : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE OR ALTER PROCEDURE ingest.usp_partition_backfill_step
                @batch_size int = 20000
            AS
            BEGIN
                SET NOCOUNT ON;
                SET XACT_ABORT ON;

                IF @batch_size < 1000 OR @batch_size > 100000
                    THROW 51000, 'usp_partition_backfill_step: @batch_size must be between 1000 and 100000.', 1;

                IF OBJECT_ID(N'ingest.vendor_transactions', N'U') IS NULL
                    THROW 51000, 'usp_partition_backfill_step: ingest.vendor_transactions not found.', 1;

                IF OBJECT_ID(N'ingest.vendor_transactions_p', N'U') IS NULL
                BEGIN
                    -- Cutover already completed or shadow table missing.
                    RETURN;
                END

                BEGIN TRANSACTION;

                ;WITH src AS
                (
                    SELECT TOP (@batch_size)
                        s.id,
                        s.vendor_id,
                        s.vendor_reference,
                        s.brand_id,
                        s.player_id,
                        s.amount_cents,
                        s.currency,
                        s.kind,
                        s.status,
                        s.round_id,
                        s.description,
                        s.occurred_at,
                        s.received_at,
                        s.rejected_reason,
                        s.created_at,
                        s.created_by_type,
                        s.created_by_id,
                        s.last_modified_at,
                        s.last_modified_by_type,
                        s.last_modified_by_id
                    FROM ingest.vendor_transactions s WITH (READPAST)
                    WHERE NOT EXISTS
                    (
                        SELECT 1
                        FROM ingest.vendor_transactions_p p WITH (READPAST)
                        WHERE p.id = s.id
                    )
                    ORDER BY s.received_at ASC, s.id ASC
                )
                INSERT INTO ingest.vendor_transactions_p
                (
                    id,
                    vendor_id,
                    vendor_reference,
                    brand_id,
                    player_id,
                    amount_cents,
                    currency,
                    kind,
                    status,
                    round_id,
                    description,
                    occurred_at,
                    received_at,
                    rejected_reason,
                    created_at,
                    created_by_type,
                    created_by_id,
                    last_modified_at,
                    last_modified_by_type,
                    last_modified_by_id
                )
                SELECT
                    id,
                    vendor_id,
                    vendor_reference,
                    brand_id,
                    player_id,
                    amount_cents,
                    currency,
                    kind,
                    status,
                    round_id,
                    description,
                    occurred_at,
                    received_at,
                    rejected_reason,
                    created_at,
                    created_by_type,
                    created_by_id,
                    last_modified_at,
                    last_modified_by_type,
                    last_modified_by_id
                FROM src;

                DECLARE @inserted int = @@ROWCOUNT;
                COMMIT TRANSACTION;

                -- Lightweight progress signal for Agent history.
                PRINT CONCAT('ingest.usp_partition_backfill_step inserted rows: ', @inserted);
            END
            """
        );

        migrationBuilder.Sql(
            """
            -- SQL Server Agent is the "cron inside DB" on SQL Server.
            -- This block is best-effort: it provisions the Agent job
            -- when Agent is available and enabled, and silently skips on
            -- environments where it isn't (Linux containers with Agent
            -- service stopped, restricted hosts, dev/CI). Production
            -- Windows hosts get the job; dev gets the stored procedure
            -- only — schedule the backfill manually or via a HostedService.
            IF DB_ID(N'msdb') IS NULL
                RETURN;

            IF OBJECT_ID(N'msdb.dbo.sp_add_job', N'P') IS NULL
                RETURN;

            -- Agent XPs disabled (Linux containers default to this) means
            -- sp_add_job will throw at runtime even though it exists.
            -- Check the config and bail before we try.
            DECLARE @agentXps int;
            SELECT @agentXps = CAST(value_in_use AS int)
                FROM sys.configurations WHERE name = N'Agent XPs';
            IF (@agentXps IS NULL OR @agentXps = 0)
            BEGIN
                PRINT 'Agent XPs disabled; skipping pam_ingest_partition_backfill job provisioning. '
                    + 'Enable Agent + Agent XPs in your environment to schedule the backfill, '
                    + 'or run ingest.usp_partition_backfill_step from a HostedService / cron.';
                RETURN;
            END

            DECLARE @jobName sysname = N'pam_ingest_partition_backfill';
            DECLARE @scheduleName sysname = N'Every 5 Minutes';
            DECLARE @dbName sysname = DB_NAME();
            DECLARE @ownerLogin sysname = SUSER_SNAME();
            DECLARE @jobId uniqueidentifier;

            -- Defensive try/catch so any Agent failure (service stopped
            -- mid-migration, permissions missing, etc.) does not abort
            -- the whole migration. The job provisioning is an operational
            -- convenience, not a correctness requirement.
            BEGIN TRY
                IF EXISTS (SELECT 1 FROM msdb.dbo.sysjobs WHERE name = @jobName)
                BEGIN
                    EXEC msdb.dbo.sp_delete_job
                        @job_name = @jobName,
                        @delete_unused_schedule = 1;
                END

                EXEC msdb.dbo.sp_add_job
                    @job_name = @jobName,
                    @enabled = 1,
                    @description = N'Incremental backfill from ingest.vendor_transactions to ingest.vendor_transactions_p until partition cutover.',
                    @owner_login_name = @ownerLogin,
                    @job_id = @jobId OUTPUT;

                EXEC msdb.dbo.sp_add_jobstep
                    @job_id = @jobId,
                    @step_name = N'run_backfill_step',
                    @subsystem = N'TSQL',
                    @database_name = @dbName,
                    @command = N'EXEC ingest.usp_partition_backfill_step @batch_size = 20000;',
                    @on_success_action = 1,
                    @on_fail_action = 2;

                EXEC msdb.dbo.sp_add_schedule
                    @schedule_name = @scheduleName,
                    @enabled = 1,
                    @freq_type = 4,             -- daily
                    @freq_interval = 1,
                    @freq_subday_type = 4,      -- minutes
                    @freq_subday_interval = 5,  -- every 5 minutes
                    @active_start_time = 0;     -- 00:00:00

                EXEC msdb.dbo.sp_attach_schedule
                    @job_id = @jobId,
                    @schedule_name = @scheduleName;

                EXEC msdb.dbo.sp_add_jobserver
                    @job_id = @jobId;
            END TRY
            BEGIN CATCH
                DECLARE @msg nvarchar(2048) = CONCAT(
                    'Agent job provisioning skipped: ',
                    ERROR_NUMBER(), ' / ', ERROR_MESSAGE());
                PRINT @msg;
            END CATCH
            """
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF DB_ID(N'msdb') IS NOT NULL
               AND OBJECT_ID(N'msdb.dbo.sp_delete_job', N'P') IS NOT NULL
               AND EXISTS (SELECT 1 FROM msdb.dbo.sysjobs WHERE name = N'pam_ingest_partition_backfill')
            BEGIN
                EXEC msdb.dbo.sp_delete_job
                    @job_name = N'pam_ingest_partition_backfill',
                    @delete_unused_schedule = 1;
            END
            """
        );

        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'ingest.usp_partition_backfill_step', N'P') IS NOT NULL
                DROP PROCEDURE ingest.usp_partition_backfill_step;
            """
        );
    }
}
