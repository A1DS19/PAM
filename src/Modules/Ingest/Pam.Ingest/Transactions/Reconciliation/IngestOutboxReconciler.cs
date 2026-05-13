using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pam.Ingest.Contracts.Transactions.IntegrationEvents;
using Pam.Ingest.Contracts.Transactions.Models;
using Pam.Ingest.Data;
using Pam.Ingest.Transactions.EventHandlers;
using Pam.Shared.Messaging.Data;
using Pam.Shared.Messaging.Reconciliation;
using Pam.Shared.Time;

namespace Pam.Ingest.Transactions.Reconciliation;

// Defensive backstop for the Ingest atomic-outbox path. Finds
// vendor_transactions rows whose TransactionIngestedIntegrationEvent never
// produced an outbox_dispatched_log entry, and republishes them through
// the normal Publish + DispatchedLog.Add + SaveChanges path.
//
// What this catches that AtomicOutboxBehavior can't:
//   - Crashes in the narrow window between AtomicOutboxBehavior.CommitAsync
//     completing and BusOutboxNotification firing.
//   - Future regressions to the atomicity invariant (e.g. a code path
//     that writes a VendorTransaction outside the MediatR pipeline).
//   - Hardware faults or network partitions that prevent dispatch.
//
// At-least-once delivery; consumers must dedupe via MT's
// UseEntityFrameworkOutbox(context) inbox.
public sealed class IngestOutboxReconciler(
    IngestDbContext business,
    PamMessagingDbContext messaging,
    IPublishEndpoint publisher,
    IClock clock,
    ILogger<IngestOutboxReconciler> logger
) : IOutboxReconciler
{
    // Bounded batch size — a runaway hole (thousands of orphans from a
    // multi-hour outage) gets chipped at over multiple passes instead of
    // hammering the DB in one query.
    private const int MaxBatchSize = 200;

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
            )
            .OrderBy(t => t.ReceivedAt)
            .Take(MaxBatchSize)
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            return 0;
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

        if (orphans.Count == 0)
        {
            return 0;
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

        return orphans.Count;
    }
}
