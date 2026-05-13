using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Pam.Shared.Messaging.Reconciliation;

// Background service that periodically asks every registered IOutboxReconciler
// to scan its module's business tables for orphans (rows that committed but
// whose integration event never landed) and republish them.
//
// The atomic outbox transaction (AtomicOutboxBehavior) is the primary
// correctness mechanism — this service is the defensive backstop for failure
// modes that mechanism can't cover (hardware faults, network partitions
// during broker delivery, future regressions to the atomicity guarantee).
//
// Failures in one reconciler do not block the others. Failures are logged
// at warn-level; they do not propagate or stop the loop.
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

        logger.LogInformation(
            "OutboxReconciliationService running with interval={Interval} minAge={MinAge}",
            opts.Interval,
            opts.MinAge
        );

        // Run once on startup (catches rows left orphaned by a previous
        // process that crashed mid-commit), then on every interval.
        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOnceAsync(opts.MinAge, stoppingToken);

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

    private async Task RunOnceAsync(TimeSpan minAge, CancellationToken ct)
    {
        // Resolve reconcilers in a fresh scope. Reconcilers depend on
        // scoped DbContexts (business + messaging) and IPublishEndpoint —
        // a new scope per pass means clean change trackers and a fresh
        // connection from the pool.
        await using var scope = scopeFactory.CreateAsyncScope();
        var reconcilers = scope.ServiceProvider.GetServices<IOutboxReconciler>().ToList();
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
                var republished = await reconciler.ScanAndRepublishAsync(minAge, ct);
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
}
