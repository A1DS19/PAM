using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pam.Ingest.Data;

namespace Pam.Ingest.Transactions.Partitioning;

// Daily lifecycle maintenance for ingest.vendor_transactions.
// Calls ingest.usp_partition_maintain_vendor_transactions, which is the
// canonical idempotent SP installed by migration 4. Runs once at startup,
// then every 24 hours. No SQL Agent dependency — portable to Linux/k8s.
//
// Errors are logged and swallowed: a transient SQL failure shouldn't take
// the API down, and the next tick retries.
public sealed class PartitionMaintenanceService(
    IServiceScopeFactory scopes,
    ILogger<PartitionMaintenanceService> logger
) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    private const int FutureWeeksToKeep = 12;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        while (true)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Partition maintenance tick failed; will retry on next interval"
                );
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken))
            {
                return;
            }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IngestDbContext>();

        await db.Database.ExecuteSqlRawAsync(
            $"EXEC ingest.usp_partition_maintain_vendor_transactions @future_weeks_to_keep = {FutureWeeksToKeep};",
            ct
        );

        logger.LogInformation("Partition maintenance tick completed");
    }
}
