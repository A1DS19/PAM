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
//     event and call IPublishEndpoint.Publish; they also Add an
//     OutboxDispatchedLog row to PamMessagingDbContext so the reconciler
//     can detect orphans later.
//
//  2. The single .UseBusOutbox() registration in AddPamMassTransit binds
//     PamMessagingDbContext as the bus-wide outbox target — every Publish
//     call writes an OutboxMessage row to PamMessagingDbContext's change
//     tracker (regardless of which module's save is currently running).
//
//  3. The business SaveChanges completes — only the business rows are
//     persisted. The OutboxMessage and OutboxDispatchedLog rows are still
//     sitting in PamMessagingDbContext's in-memory change tracker.
//
//  4. This behavior calls PamMessagingDbContext.SaveChangesAsync, committing
//     the outbox + dispatched-log rows. The BusOutboxDeliveryService polls
//     the table and forwards messages to RabbitMQ.
//
// Atomicity caveat — and how we cover it: business commit (step 3) and
// outbox commit (step 4) run in SEPARATE transactions. A crash between (3)
// and (4) leaves the business row persisted but the integration event
// undelivered — sub-millisecond under-deliver window. We attempted to
// close this by sharing a SqlConnection + IDbContextTransaction across
// the business + messaging DbContexts; it fights EF Core's connection
// model when handlers query the DB before calling SaveChanges (the
// context opens its own connection independently of the ambient txn, and
// UseTransaction after the fact errors with "transaction is not
// associated with the current connection"). MT 8.5's one-DbContext-per-
// bus constraint also rules out the canonical per-module-outbox fix; MT
// 9.1's multi-DbContext outbox is off-limits per the Apache-2.0 license
// pin (ADR #5). The OutboxReconciliationService is the defensive backstop
// that closes the practical risk: every business row whose dispatched-log
// entry is missing gets republished on the next sweep (5 min default).
// See DECISIONS.md ADR #28 for the design rationale.
//
// Pipeline position: innermost behavior, after AuditBehavior. If the flush
// throws, audit records the failure rather than a misleading "success".
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
            // COMMIT #2 — outbox + dispatched-log. CancellationToken.None
            // is deliberate: once `next()` returned, COMMIT #1 already
            // landed the business row, and we MUST flush the outbox to
            // keep the two tiers consistent. Honouring the request CT
            // here would manufacture orphans every time a client
            // disconnects in the microsecond window between #1 and #2
            // — the reconciler would mop them up, but at the cost of
            // at-least-once republishes every consumer has to dedupe.
            // The flush is bounded by the SQL command timeout; it does
            // not run forever just because the client gave up.
            var written = await messaging.SaveChangesAsync(CancellationToken.None);
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
