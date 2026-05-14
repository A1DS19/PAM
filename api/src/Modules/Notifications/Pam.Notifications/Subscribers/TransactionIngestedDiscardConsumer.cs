using MassTransit;
using Microsoft.Extensions.Logging;
using Pam.Ingest.Contracts.Transactions.IntegrationEvents;

namespace Pam.Notifications.Subscribers;

// Placeholder subscriber for TransactionIngestedIntegrationEvent. Logs +
// discards. Exists so the integration-event exchange has a bound queue
// in every environment — without it, RabbitMQ route-and-drops the
// message at the exchange and queue depth / consumer lag / dispatch
// failures are invisible.
//
// Replace the body (not the registration) when a real subscriber lands
// (transactional receipts, wallet projection, settlement, …). Keeping
// the queue bound from day one means the consumer swap doesn't require
// a topology change, and integration-event shape regressions surface
// here instead of leaking out the day the first real consumer ships.
public sealed class TransactionIngestedDiscardConsumer(
    ILogger<TransactionIngestedDiscardConsumer> logger
) : IConsumer<TransactionIngestedIntegrationEvent>
{
    public Task Consume(ConsumeContext<TransactionIngestedIntegrationEvent> context)
    {
        logger.LogDebug(
            "Discarded TransactionIngestedIntegrationEvent {TransactionId}",
            context.Message.TransactionId
        );
        return Task.CompletedTask;
    }
}
