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
// the normal Publish + DispatchedLog.Add + SaveChanges path. See ADR #28
// for why two commits exist and what gap this closes.
//
// Scan shape: a SQL LEFT JOIN against messaging.outbox_dispatched_log,
// returning ONLY orphan rows. The per-cycle row budget (ScanBatchSize *
// MaxScanBatchesPerCycle) therefore bounds the ORPHAN count, not the
// total scan depth. The old in-memory diff was bounded by total rows in
// the window — once a stress-test backlog grew past ~40k rows, orphans
// past the budget became permanently invisible because the cursor reset
// every cycle (caught 2026-05-13).
public sealed class IngestOutboxReconciler(
    IngestDbContext business,
    PamMessagingDbContext messaging,
    IPublishEndpoint publisher,
    IClock clock,
    IOptions<IngestReconciliationOptions> options,
    ILogger<IngestOutboxReconciler> logger
) : IOutboxReconciler
{
    private const string EventType = nameof(TransactionIngestedIntegrationEvent);

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
        var scanBatchSize = Math.Clamp(options.Value.ScanBatchSize, 100, 10_000);
        var maxBatches = Math.Clamp(options.Value.MaxScanBatchesPerCycle, 1, 500);
        var moduleName = ModuleName;
        var totalRepublished = 0;

        // Each iteration republishes one batch of orphans. After SaveChanges,
        // the rows just published now have dispatched_log entries, so the
        // next query naturally returns the NEXT batch of orphans — no
        // cursor state needed. Loop terminates when the query returns no
        // rows OR maxBatches is hit (incident-safety circuit-breaker).
        for (var batch = 0; batch < maxBatches; batch++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            // SQL LEFT JOIN: only orphan rows come back. The join key on
            // l.business_pk computes the dispatched_log primary-key form
            // of t.id (Guid.ToString("N") = lowercase hex no dashes).
            // SQL Server's CONVERT on uniqueidentifier returns upper-case
            // with dashes by default — LOWER+REPLACE produces the matching
            // shape so the PK seek on (module, event_type, business_pk)
            // succeeds row-by-row.
            //
            // ix_vendor_transactions_received_at_status keeps the outer
            // scan O(window-size) over t. The inner seek into
            // outbox_dispatched_log is PK-seek-per-row. Plan is index-
            // served end-to-end at production scale.
            var orphans = await business
                .Database.SqlQuery<OrphanRow>(
                    $@"
                    SELECT TOP ({scanBatchSize})
                        t.id,
                        t.vendor_id,
                        t.vendor_reference,
                        t.brand_id,
                        t.player_id,
                        t.amount_cents,
                        t.currency,
                        t.kind,
                        t.status,
                        t.round_id,
                        t.occurred_at
                    FROM ingest.vendor_transactions t
                    LEFT JOIN messaging.outbox_dispatched_log l
                        ON l.module      = {moduleName}
                       AND l.event_type  = {EventType}
                       AND l.business_pk = LOWER(REPLACE(CONVERT(varchar(36), t.id), '-', ''))
                    WHERE t.received_at > {lookback}
                      AND t.received_at < {threshold}
                      AND t.status <> 'Rejected'
                      AND l.business_pk IS NULL
                    ORDER BY t.received_at, t.id
                "
                )
                .ToListAsync(cancellationToken);

            if (orphans.Count == 0)
            {
                break;
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
                        Kind: Enum.Parse<TransactionKind>(tx.Kind),
                        Status: Enum.Parse<TransactionStatus>(tx.Status),
                        RoundId: tx.RoundId,
                        TransactionOccurredAt: tx.OccurredAt
                    ),
                    cancellationToken
                );

                messaging.DispatchedLog.Add(
                    new OutboxDispatchedLog
                    {
                        Module = moduleName,
                        BusinessPk = tx.Id.ToString("N"),
                        EventType = EventType,
                        DispatchedAt = clock.UtcNow,
                    }
                );

                logger.LogWarning(
                    "Republished orphan {EventType} for VendorTransaction {TransactionId}",
                    EventType,
                    tx.Id
                );
            }

            // Single commit for the batch. The DispatchedLog rows + the
            // OutboxMessage rows queued by Publish() commit together so
            // the next iteration's query won't re-detect these as orphans.
            await messaging.SaveChangesAsync(cancellationToken);
            totalRepublished += orphans.Count;

            // Short batch → no more work this cycle; otherwise loop.
            if (orphans.Count < scanBatchSize)
            {
                break;
            }
        }

        return totalRepublished;
    }

    // Unmapped projection for SqlQuery<T>. EF Core 8+ accepts any type
    // whose property names match the result column aliases — positional
    // records work because each parameter becomes a matching init-only
    // property. Enums round-trip as strings (the DB columns are nvarchar
    // via HasConversion<string>() in VendorTransactionConfiguration) so
    // we parse them on materialise.
    private sealed record OrphanRow(
        Guid Id,
        string VendorId,
        string VendorReference,
        Guid BrandId,
        Guid PlayerId,
        long AmountCents,
        string Currency,
        string Kind,
        string Status,
        string? RoundId,
        DateTimeOffset OccurredAt
    );
}
