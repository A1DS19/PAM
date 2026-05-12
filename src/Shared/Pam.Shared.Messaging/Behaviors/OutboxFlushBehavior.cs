using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pam.Shared.Contracts.CQRS;
using Pam.Shared.Messaging.Data;

namespace Pam.Shared.Messaging.Behaviors;

// Flushes PamMessagingDbContext at the tail of every command pipeline.
//
// The flow:
//
//  1. Command handler runs, writes to its module's business DbContext, calls
//     SaveChangesAsync. The DispatchDomainEventsInterceptor fires (pre-save),
//     dispatches domain events via MediatR. Bridge handlers receive each
//     event and call IPublishEndpoint.Publish.
//
//  2. The single .UseBusOutbox() registration in AddPamMassTransit binds
//     PamMessagingDbContext as the bus-wide outbox target — every Publish
//     call writes an OutboxMessage row to PamMessagingDbContext's change
//     tracker (regardless of which module's save is currently running).
//
//  3. The business SaveChanges completes — only the business rows are
//     persisted. The OutboxMessage rows are still sitting in
//     PamMessagingDbContext's in-memory change tracker.
//
//  4. This behavior calls PamMessagingDbContext.SaveChangesAsync, committing
//     the outbox rows. The BusOutboxDeliveryService polls the table and
//     forwards messages to RabbitMQ.
//
// Atomicity caveat: business commit (step 3) and outbox commit (step 4) run
// in SEPARATE transactions. A crash between (3) and (4) leaves the business
// row persisted but the integration event undelivered — an under-deliver
// failure mode. True atomicity requires a shared connection + shared
// transaction across both DbContexts (or MT 9.1's per-DbContext outbox,
// which we can't use while the project holds the Apache-2.0 line). The
// under-deliver risk is bounded by the time between (3) and (4) — typically
// sub-millisecond — and is documented as an accepted trade-off in
// docs/DECISIONS.md. A reconciliation job lands as a follow-up.
//
// Why this is INNERMOST (after AuditBehavior): if the flush throws, audit
// must record the failure, not a misleading "success".
public sealed class OutboxFlushBehavior<TRequest, TResponse>(
    PamMessagingDbContext messaging,
    ILogger<OutboxFlushBehavior<TRequest, TResponse>> logger
) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken
    )
    {
        // Queries don't publish; skip the change-tracker probe to keep the
        // pipeline lean. Commands fall through to the flush.
        if (!IsCommand(request))
        {
            return await next();
        }

        var response = await next();

        if (messaging.ChangeTracker.HasChanges())
        {
            var written = await messaging.SaveChangesAsync(cancellationToken);
            logger.LogDebug("Flushed {Count} outbox row(s) to messaging.outbox_message", written);
        }

        return response;
    }

    private static bool IsCommand(TRequest request) =>
        request is ICommand
        || request
            .GetType()
            .GetInterfaces()
            .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>));
}
