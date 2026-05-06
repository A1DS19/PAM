using MediatR;
using Microsoft.Extensions.Logging;
using Pam.Players.Players.Events;
using Pam.Shared.DDD;

namespace Pam.Players.Players.EventHandlers;

public sealed class PlayerRegisteredDomainHandler(ILogger<PlayerRegisteredDomainHandler> logger)
    : INotificationHandler<DomainEventNotification<PlayerRegisteredDomainEvent>>
{
    public Task Handle(
        DomainEventNotification<PlayerRegisteredDomainEvent> notification,
        CancellationToken cancellationToken
    )
    {
        var ev = notification.Event;
        logger.LogInformation(
            "Player registered: PlayerId={PlayerId} BrandId={BrandId} IdentityProviderId={IdentityProviderId} Jurisdiction={Jurisdiction}",
            ev.PlayerId,
            ev.BrandId,
            ev.IdentityProviderId,
            ev.Jurisdiction
        );
        return Task.CompletedTask;
    }
}
