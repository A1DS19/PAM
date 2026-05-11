using MassTransit;
using MediatR;
using Pam.Ingest.Contracts.Transactions.IntegrationEvents;
using Pam.Ingest.Transactions.Events;
using Pam.Shared.DDD;

namespace Pam.Ingest.Transactions.EventHandlers;

// Bridges the in-module domain event to the cross-module integration event.
// Publish goes through MassTransit's EF Core outbox (configured by
// IngestModule.ConfigureOutbox); it commits atomically with the
// VendorTransaction row in the same transaction. A throwing handler here
// rolls back the SaveChanges that triggered it — see ARCHITECTURE.md
// "Outbox + pre-save domain-event dispatch".
public sealed class TransactionIngestedDomainHandler(IPublishEndpoint publisher)
    : INotificationHandler<DomainEventNotification<TransactionIngestedDomainEvent>>
{
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
    }
}
