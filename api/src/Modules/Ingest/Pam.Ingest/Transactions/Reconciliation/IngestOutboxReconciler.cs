using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pam.Ingest.Contracts.Transactions.IntegrationEvents;
using Pam.Ingest.Contracts.Transactions.Models;
using Pam.Ingest.Data;
using Pam.Ingest.Transactions.EventHandlers;
using Pam.Shared.Messaging.Data;
using Pam.Shared.Messaging.Reconciliation;
using Pam.Shared.Time;

namespace Pam.Ingest.Transactions.Reconciliation;

// Defensive backstop for the Ingest two-commit publish path. Finds
// vendor_transactions rows whose TransactionIngestedIntegrationEvent never
// produced an outbox_dispatched_log entry, and republishes them through
// the normal Publish + DispatchedLog.Add + SaveChanges path.
//
// What this catches that OutboxFlushBehavior cannot:
//   - Crashes between business COMMIT #1 (IngestDbContext) and messaging
//     COMMIT #2 (PamMessagingDbContext via OutboxFlushBehavior). See ADR #28
//     for why those are not a single transaction.
//   - Code paths that write a VendorTransaction outside the MediatR pipeline
//     (e.g. tooling, future bulk-import jobs, or a regression that bypasses
//     the bridge handler).
//   - Hardware faults or network partitions that prevent dispatch.
//
// At-least-once delivery; consumers must dedupe via MT's
// UseEntityFrameworkOutbox(context) inbox.
public sealed class IngestOutboxReconciler(
    IngestDbContext business,
    PamMessagingDbContext messaging,
    IPublishEndpoint publisher,
    IClock clock,
    IOptions<IngestReconciliationOptions> options,
    ILogger<IngestOutboxReconciler> logger
) : IOutboxReconciler
{
    public string ModuleName => TransactionIngestedDomainHandler.ModuleName;

    public async Task<int> ScanAndRepublishAsync(
        TimeSpan minAge,
        TimeSpan lookbackWindow,
        CancellationToken cancellationToken
    )
    {
        var now = clock.UtcNow;
        var threshold = now.Subtract(minAge);
        var lookback = now.Subtract(lookbackWindow);
        var eventType = nameof(TransactionIngestedIntegrationEvent);
        var scanBatchSize = Math.Clamp(options.Value.ScanBatchSize, 100, 10_000);
        var maxBatches = Math.Clamp(options.Value.MaxScanBatchesPerCycle, 1, 500);
        var totalRepublished = 0;
        // Composite keyset cursor on (ReceivedAt, Id). ReceivedAt alone is
        // unsafe at multi-thousand TPS: sub-millisecond clock ties at a
        // batch boundary can permanently skip rows once they age past
        // lookbackWindow. UUIDv7 is time-ordered, so within a ReceivedAt
        // tie the Id ordering is also stable.
        DateTimeOffset? cursorReceivedAt = null;
        Guid? cursorId = null;

        for (var batch = 0; batch < maxBatches; batch++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            // Two-step diff. We can't join across DbContexts in LINQ — EF
            // compiles to SQL per-context. Same physical database, but
            // different change trackers and connections at the EF layer.
            //
            // Bounded scan: only rows in (lookback, threshold) are
            // candidates. The supporting index
            // ix_vendor_transactions_received_at_status keeps each pass
            // O(window-size) rather than O(table-size) — critical at
            // millions of rows/day. Rejected rows raise no event by design
            // (RecordRejected does not call RaiseDomainEvent) — skip them.
            var candidates = await business
                .VendorTransactions.AsNoTracking()
                .Where(t =>
                    t.ReceivedAt > lookback
                    && t.ReceivedAt < threshold
                    && t.Status != TransactionStatus.Rejected
                    && (
                        cursorReceivedAt == null
                        || t.ReceivedAt > cursorReceivedAt
                        || (t.ReceivedAt == cursorReceivedAt && t.Id > cursorId)
                    )
                )
                .OrderBy(t => t.ReceivedAt)
                .ThenBy(t => t.Id)
                .Take(scanBatchSize)
                .ToListAsync(cancellationToken);

            if (candidates.Count == 0)
            {
                break;
            }

            var candidatePks = candidates
                .Select(t => t.Id.ToString("N"))
                .ToHashSet(StringComparer.Ordinal);

            var coveredPks = await messaging
                .DispatchedLog.AsNoTracking()
                .Where(l =>
                    l.Module == TransactionIngestedDomainHandler.ModuleName
                    && l.EventType == eventType
                    && candidatePks.Contains(l.BusinessPk)
                )
                .Select(l => l.BusinessPk)
                .ToListAsync(cancellationToken);

            var coveredSet = coveredPks.ToHashSet(StringComparer.Ordinal);
            var orphans = candidates.Where(t => !coveredSet.Contains(t.Id.ToString("N"))).ToList();

            var last = candidates[^1];
            cursorReceivedAt = last.ReceivedAt;
            cursorId = last.Id;

            if (orphans.Count == 0)
            {
                if (candidates.Count < scanBatchSize)
                {
                    break;
                }
                continue;
            }

            foreach (var tx in orphans)
            {
                await publisher.Publish(
                    new TransactionIngestedIntegrationEvent(
                        TransactionId: tx.Id,
                        VendorId: tx.VendorId,
                        VendorReference: tx.VendorReference,
                        BrandId: tx.BrandId,
                        PlayerId: tx.PlayerId,
                        AmountCents: tx.AmountCents,
                        Currency: tx.Currency,
                        Kind: tx.Kind,
                        Status: tx.Status,
                        RoundId: tx.RoundId,
                        TransactionOccurredAt: tx.OccurredAt
                    ),
                    cancellationToken
                );

                messaging.DispatchedLog.Add(
                    new OutboxDispatchedLog
                    {
                        Module = ModuleName,
                        BusinessPk = tx.Id.ToString("N"),
                        EventType = eventType,
                        DispatchedAt = clock.UtcNow,
                    }
                );

                logger.LogWarning(
                    "Republished orphan {EventType} for VendorTransaction {TransactionId}",
                    eventType,
                    tx.Id
                );
            }

            // Single commit for the batch. The DispatchedLog rows + the
            // OutboxMessage rows queued by Publish() commit together so the
            // next pass won't re-detect these as orphans.
            await messaging.SaveChangesAsync(cancellationToken);
            totalRepublished += orphans.Count;

            // Keep cycles bounded even with large historical gaps.
            if (candidates.Count < scanBatchSize)
            {
                break;
            }
        }
        return totalRepublished;
    }
}
