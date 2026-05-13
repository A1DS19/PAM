using MassTransit;
using Microsoft.Extensions.Logging;
using Pam.Ingest.Contracts.Transactions.IntegrationEvents;

namespace Pam.Notifications.Stress;

// Stress-test-only sink. Registered ONLY when Stress:DiscardConsumers:Enabled
// is true (the consumer filter in AddPamMassTransit excludes the
// Pam.Notifications.Stress namespace otherwise).
//
// Why this exists: without any bound queue, RabbitMQ routes-and-drops at
// the integration-event exchange — you can't observe queue depth, consumer
// lag, or end-to-end settlement. A no-op consumer materialises the queue
// so stress runs measure the same topology shape production will have once
// real consumers ship, without contaminating the numbers with work the
// consumer would do (SMTP latency, projection writes, …).
//
// Do not promote this to a real consumer. When a real
// TransactionIngestedIntegrationEvent consumer lands, this file deletes.
public sealed class TransactionIngestedDiscardConsumer(
    ILogger<TransactionIngestedDiscardConsumer> logger
) : IConsumer<TransactionIngestedIntegrationEvent>
{
    public Task Consume(ConsumeContext<TransactionIngestedIntegrationEvent> context)
    {
        logger.LogDebug(
            "[stress] discarded TransactionIngestedIntegrationEvent {TransactionId}",
            context.Message.TransactionId
        );
        return Task.CompletedTask;
    }
}
