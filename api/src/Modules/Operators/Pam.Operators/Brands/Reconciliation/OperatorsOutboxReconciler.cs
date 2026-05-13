using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pam.Operators.Brands.EventHandlers;
using Pam.Operators.Contracts.Brands.IntegrationEvents;
using Pam.Operators.Data;
using Pam.Shared.Messaging.Data;
using Pam.Shared.Messaging.Reconciliation;
using Pam.Shared.Time;

namespace Pam.Operators.Brands.Reconciliation;

// Defensive backstop for the Operators two-commit publish path. Same
// shape as IngestOutboxReconciler — see that file for the full
// rationale. Brand volume is trivial compared to vendor ingest, but the
// scan strategy is identical: SQL LEFT JOIN that returns only orphan
// rows, so the per-cycle budget bounds the orphan count rather than the
// total row count.
public sealed class OperatorsOutboxReconciler(
    OperatorsDbContext business,
    PamMessagingDbContext messaging,
    IPublishEndpoint publisher,
    IClock clock,
    IOptions<OperatorsReconciliationOptions> options,
    ILogger<OperatorsOutboxReconciler> logger
) : IOutboxReconciler
{
    private const string EventType = nameof(BrandCreatedIntegrationEvent);

    public string ModuleName => BrandCreatedDomainHandler.ModuleName;

    public async Task<int> ScanAndRepublishAsync(
        TimeSpan minAge,
        TimeSpan lookbackWindow,
        CancellationToken cancellationToken
    )
    {
        var now = clock.UtcNow;
        var threshold = now.Subtract(minAge);
        var lookback = now.Subtract(lookbackWindow);
        var scanBatchSize = Math.Clamp(options.Value.ScanBatchSize, 50, 5_000);
        var maxBatches = Math.Clamp(options.Value.MaxScanBatchesPerCycle, 1, 50);
        var moduleName = ModuleName;
        var totalRepublished = 0;

        for (var batch = 0; batch < maxBatches; batch++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            // Same LEFT JOIN trick as IngestOutboxReconciler — orphan
            // filter at SQL means each loop iteration sees a fresh batch
            // of orphans without cursor state.
            var orphans = await business
                .Database.SqlQuery<OrphanRow>(
                    $@"
                    SELECT TOP ({scanBatchSize})
                        b.id,
                        b.slug,
                        b.jurisdiction
                    FROM operators.brands b
                    LEFT JOIN messaging.outbox_dispatched_log l
                        ON l.module      = {moduleName}
                       AND l.event_type  = {EventType}
                       AND l.business_pk = LOWER(REPLACE(CONVERT(varchar(36), b.id), '-', ''))
                    WHERE b.created_at > {lookback}
                      AND b.created_at < {threshold}
                      AND l.business_pk IS NULL
                    ORDER BY b.created_at, b.id
                "
                )
                .ToListAsync(cancellationToken);

            if (orphans.Count == 0)
            {
                break;
            }

            foreach (var brand in orphans)
            {
                await publisher.Publish(
                    new BrandCreatedIntegrationEvent(brand.Id, brand.Slug, brand.Jurisdiction),
                    cancellationToken
                );

                messaging.DispatchedLog.Add(
                    new OutboxDispatchedLog
                    {
                        Module = moduleName,
                        BusinessPk = brand.Id.ToString("N"),
                        EventType = EventType,
                        DispatchedAt = clock.UtcNow,
                    }
                );

                logger.LogWarning(
                    "Republished orphan {EventType} for Brand {BrandId}",
                    EventType,
                    brand.Id
                );
            }

            await messaging.SaveChangesAsync(cancellationToken);
            totalRepublished += orphans.Count;

            if (orphans.Count < scanBatchSize)
            {
                break;
            }
        }

        return totalRepublished;
    }

    private sealed record OrphanRow(Guid Id, string Slug, string Jurisdiction);
}
