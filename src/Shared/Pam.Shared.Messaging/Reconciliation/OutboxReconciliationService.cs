using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pam.Shared.Messaging.Data;

namespace Pam.Shared.Messaging.Reconciliation;

// Background service that periodically asks every registered IOutboxReconciler
// to scan its module's business tables for orphans (rows that committed but
// whose integration event never landed) and republish them.
//
// Two passes per cycle:
//
//   1. Reconciliation. Each module's IOutboxReconciler scans the
//      (now - LookbackWindow, now - MinAge) window for orphans and
//      republishes. Bounded scan keeps the work O(window) regardless of
//      table size — important at millions of business rows per day.
//
//   2. Cleanup. Batched DELETE TOP N from messaging.outbox_dispatched_log
//      removes rows older than RetentionWindow. Without this the table
//      grows 1:1 with business event volume forever; with it, rows leave
//      the table once they're outside any conceivable reconciler scope.
//
// Failures in one reconciler do not block the others. Cleanup failures
// don't fail the reconciliation pass. All failures are logged at warn-
// level; they do not propagate or stop the loop.
public sealed class OutboxReconciliationService(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxReconciliationOptions> options,
    ILogger<OutboxReconciliationService> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        if (!opts.Enabled)
        {
            logger.LogInformation(
                "OutboxReconciliationService disabled via {Section}",
                OutboxReconciliationOptions.SectionName
            );
            return;
        }

        if (opts.RetentionWindow < opts.LookbackWindow)
        {
            logger.LogError(
                "RetentionWindow ({Retention}) must be >= LookbackWindow ({Lookback}). "
                    + "Service will not start — reconciler depends on dispatched-log rows "
                    + "being present for every business row inside its scan window.",
                opts.RetentionWindow,
                opts.LookbackWindow
            );
            return;
        }

        logger.LogInformation(
            "OutboxReconciliationService running with interval={Interval} "
                + "minAge={MinAge} lookback={Lookback} retention={Retention}",
            opts.Interval,
            opts.MinAge,
            opts.LookbackWindow,
            opts.RetentionWindow
        );

        // Run once on startup (catches rows left orphaned by a previous
        // process that crashed mid-commit), then on every interval.
        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOnceAsync(opts, stoppingToken);

            try
            {
                await Task.Delay(opts.Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task RunOnceAsync(OutboxReconciliationOptions opts, CancellationToken ct)
    {
        // Resolve reconcilers in a fresh scope. Reconcilers depend on
        // scoped DbContexts (business + messaging) and IPublishEndpoint —
        // a new scope per pass means clean change trackers and a fresh
        // connection from the pool.
        await using var scope = scopeFactory.CreateAsyncScope();

        await ReconcileAsync(scope.ServiceProvider, opts, ct);
        await CleanupDispatchedLogAsync(scope.ServiceProvider, opts, ct);
    }

    private async Task ReconcileAsync(
        IServiceProvider sp,
        OutboxReconciliationOptions opts,
        CancellationToken ct
    )
    {
        var reconcilers = sp.GetServices<IOutboxReconciler>().ToList();
        if (reconcilers.Count == 0)
        {
            return;
        }

        foreach (var reconciler in reconcilers)
        {
            if (ct.IsCancellationRequested)
            {
                return;
            }

            try
            {
                var republished = await reconciler.ScanAndRepublishAsync(
                    opts.MinAge,
                    opts.LookbackWindow,
                    ct
                );
                if (republished > 0)
                {
                    logger.LogWarning(
                        "Reconciler {Module} republished {Count} orphan event(s)",
                        reconciler.ModuleName,
                        republished
                    );
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(
                    ex,
                    "Reconciler {Module} threw; will retry on next interval",
                    reconciler.ModuleName
                );
            }
        }
    }

    private async Task CleanupDispatchedLogAsync(
        IServiceProvider sp,
        OutboxReconciliationOptions opts,
        CancellationToken ct
    )
    {
        try
        {
            var messaging = sp.GetRequiredService<PamMessagingDbContext>();
            var cutoff = DateTimeOffset.UtcNow.Subtract(opts.RetentionWindow);
            var totalDeleted = 0;

            for (var batch = 0; batch < opts.CleanupMaxBatchesPerCycle; batch++)
            {
                if (ct.IsCancellationRequested)
                {
                    return;
                }

                // SQL Server's DELETE TOP (N) pattern. Avoids lock
                // escalation (>5000 row locks → table lock) and keeps
                // the transaction log from ballooning on a backlog
                // sweep. DELETE TOP doesn't accept a SQL parameter for
                // the batch size, so we splice the int directly — safe
                // because it's a config-bound int we clamp ourselves,
                // never user input. The cutoff is parameterised.
                var batchSize = Math.Clamp(opts.CleanupBatchSize, 1, 50_000);
                var sql =
                    "DELETE TOP ("
                    + batchSize.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + ") FROM messaging.outbox_dispatched_log WHERE dispatched_at < {0}";
                var deleted = await messaging.Database.ExecuteSqlRawAsync(
                    sql,
                    parameters: new object[] { cutoff },
                    cancellationToken: ct
                );

                totalDeleted += deleted;

                if (deleted < batchSize)
                {
                    break;
                }
            }

            if (totalDeleted > 0)
            {
                logger.LogInformation(
                    "Cleanup removed {Count} outbox_dispatched_log row(s) older than {Cutoff}",
                    totalDeleted,
                    cutoff
                );
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex,
                "Dispatched-log cleanup threw; will retry on next interval"
            );
        }
    }
}
