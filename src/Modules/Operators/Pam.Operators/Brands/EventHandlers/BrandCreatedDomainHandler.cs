using MassTransit;
using MediatR;
using Pam.Operators.Brands.Events;
using Pam.Operators.Contracts.Brands.IntegrationEvents;
using Pam.Shared.DDD;

namespace Pam.Operators.Brands.EventHandlers;

public sealed class BrandCreatedDomainHandler(IPublishEndpoint publisher)
    : INotificationHandler<DomainEventNotification<BrandCreatedDomainEvent>>
{
    public async Task Handle(
        DomainEventNotification<BrandCreatedDomainEvent> notification,
        CancellationToken cancellationToken
    )
    {
        var e = notification.Event;
        await publisher.Publish(
            new BrandCreatedIntegrationEvent(e.BrandId, e.Name, e.Slug, e.Jurisdiction)
            {
                EventId = e.EventId,
                OccurredAt = e.OccurredAt,
            },
            cancellationToken
        );
    }
}
