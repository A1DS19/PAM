using MassTransit;
using MediatR;
using Pam.Ingest.Contracts.Transactions.IntegrationEvents;
using Pam.Ingest.Transactions.Events;
using Pam.Shared.DDD;
using Pam.Shared.Messaging.Data;
using Pam.Shared.Messaging.Reconciliation;
using Pam.Shared.Time;

namespace Pam.Ingest.Transactions.EventHandlers;

// Bridges the in-module domain event to the cross-module integration event.
//
// Durability shape (two commits, one reconciler):
//
//   COMMIT #1 — business: IngestDbContext.SaveChangesAsync persists the
//   VendorTransaction row. DispatchDomainEventsInterceptor fires THIS handler
//   pre-save, so a throw here rolls back the business write.
//
//   COMMIT #2 — messaging: OutboxFlushBehavior (innermost MediatR pipeline
//   behavior) calls PamMessagingDbContext.SaveChangesAsync at command tail.
//   Both writes staged below commit together in #2:
//     · the OutboxMessage row staged by IPublishEndpoint.Publish (MassTransit's
//       bus-wide outbox writes it through the messaging change tracker)
//     · the OutboxDispatchedLog row Added below — the reconciler's source of
//       truth for "this event was successfully queued for delivery"
//
// Cross-context single-transaction atomicity (shared SqlConnection +
// IDbContextTransaction) was attempted and reverted; see ADR #28. The
// surviving gap — crash between commit #1 and commit #2 — is closed
// asynchronously by IngestOutboxReconciler, which republishes any
// business row whose dispatched-log entry is missing.
public sealed class TransactionIngestedDomainHandler(
    IPublishEndpoint publisher,
    PamMessagingDbContext messaging,
    IClock clock
) : INotificationHandler<DomainEventNotification<TransactionIngestedDomainEvent>>
{
    public const string ModuleName = "Ingest";

    public async Task Handle(
        DomainEventNotification<TransactionIngestedDomainEvent> notification,
        CancellationToken cancellationToken
    )
    {
        var e = notification.Event;

        await publisher.Publish(
            new TransactionIngestedIntegrationEvent(
                TransactionId: e.TransactionId,
                VendorId: e.VendorId,
                VendorReference: e.VendorReference,
                BrandId: e.BrandId,
                PlayerId: e.PlayerId,
                AmountCents: e.AmountCents,
                Currency: e.Currency,
                Kind: e.Kind,
                Status: e.Status,
                RoundId: e.RoundId,
                TransactionOccurredAt: e.OccurredAt
            )
            {
                EventId = e.EventId,
                OccurredAt = ((Pam.Shared.Contracts.DDD.IDomainEvent)e).OccurredAt,
            },
            cancellationToken
        );

        messaging.DispatchedLog.Add(
            new OutboxDispatchedLog
            {
                Module = ModuleName,
                BusinessPk = e.TransactionId.ToString("N"),
                EventType = nameof(TransactionIngestedIntegrationEvent),
                DispatchedAt = clock.UtcNow,
            }
        );
    }
}
