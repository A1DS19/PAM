using MassTransit;
using Microsoft.Extensions.Logging;
using Pam.Operators.Contracts.Brands.IntegrationEvents;

namespace Pam.Notifications.Stress;

// Stress-test-only sink for BrandCreated. See
// TransactionIngestedDiscardConsumer for the full rationale.
public sealed class BrandCreatedDiscardConsumer(ILogger<BrandCreatedDiscardConsumer> logger)
    : IConsumer<BrandCreatedIntegrationEvent>
{
    public Task Consume(ConsumeContext<BrandCreatedIntegrationEvent> context)
    {
        logger.LogDebug(
            "[stress] discarded BrandCreatedIntegrationEvent {BrandId}",
            context.Message.BrandId
        );
        return Task.CompletedTask;
    }
}
