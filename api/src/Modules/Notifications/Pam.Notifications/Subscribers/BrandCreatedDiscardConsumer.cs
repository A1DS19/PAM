using MassTransit;
using Microsoft.Extensions.Logging;
using Pam.Operators.Contracts.Brands.IntegrationEvents;

namespace Pam.Notifications.Subscribers;

// Placeholder subscriber for BrandCreatedIntegrationEvent. See
// TransactionIngestedDiscardConsumer for the full rationale — same
// pattern, different event.
public sealed class BrandCreatedDiscardConsumer(ILogger<BrandCreatedDiscardConsumer> logger)
    : IConsumer<BrandCreatedIntegrationEvent>
{
    public Task Consume(ConsumeContext<BrandCreatedIntegrationEvent> context)
    {
        logger.LogDebug(
            "Discarded BrandCreatedIntegrationEvent {BrandId}",
            context.Message.BrandId
        );
        return Task.CompletedTask;
    }
}
