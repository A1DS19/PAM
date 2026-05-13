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
// Three writes participate in the same atomic transaction (held by
// AtomicOutboxBehavior at the tail of the MediatR pipeline):
//
//   1. The VendorTransaction row in ingest.vendor_transactions.
//   2. The OutboxMessage row staged by IPublishEndpoint.Publish — written
//      to messaging.outbox_message via MassTransit's bus-wide outbox.
//   3. The OutboxDispatchedLog row written here — the reconciler's source
//      of truth for "this event was successfully queued for delivery".
//
// All three commit together or all three roll back. A throwing handler rolls
// back the entire transaction.
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
