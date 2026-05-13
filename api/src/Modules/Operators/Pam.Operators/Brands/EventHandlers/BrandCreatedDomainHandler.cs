using MassTransit;
using MediatR;
using Pam.Operators.Brands.Events;
using Pam.Operators.Contracts.Brands.IntegrationEvents;
using Pam.Shared.DDD;
using Pam.Shared.Messaging.Data;
using Pam.Shared.Messaging.Reconciliation;
using Pam.Shared.Time;

namespace Pam.Operators.Brands.EventHandlers;

// Bridges the in-module domain event to the cross-module integration event.
// Same two-commit shape as Ingest's bridge handler — see ADR #28 and
// TransactionIngestedDomainHandler for the full atomicity rationale.
// Brand volume is trivial (a handful per year per region) but the
// dispatched-log write keeps Operators on the same recoverable pattern
// as every other publisher, so OperatorsOutboxReconciler can republish
// orphans on the next sweep.
public sealed class BrandCreatedDomainHandler(
    IPublishEndpoint publisher,
    PamMessagingDbContext messaging,
    IClock clock
) : INotificationHandler<DomainEventNotification<BrandCreatedDomainEvent>>
{
    public const string ModuleName = "Operators";

    public async Task Handle(
        DomainEventNotification<BrandCreatedDomainEvent> notification,
        CancellationToken cancellationToken
    )
    {
        var e = notification.Event;
        await publisher.Publish(
            new BrandCreatedIntegrationEvent(e.BrandId, e.Slug, e.Jurisdiction)
            {
                EventId = e.EventId,
                OccurredAt = e.OccurredAt,
            },
            cancellationToken
        );

        messaging.DispatchedLog.Add(
            new OutboxDispatchedLog
            {
                Module = ModuleName,
                BusinessPk = e.BrandId.ToString("N"),
                EventType = nameof(BrandCreatedIntegrationEvent),
                DispatchedAt = clock.UtcNow,
            }
        );
    }
}
