using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Pam.Shared.Contracts.DDD;
using Pam.Shared.DDD;

namespace Pam.Shared.Data.Interceptors;

public sealed class DispatchDomainEventsInterceptor(IPublisher publisher) : SaveChangesInterceptor
{
    // Cap re-dispatch generations so a handler that mutates a tracked
    // aggregate (and thus raises another event) cannot loop forever.
    private const int MaxGenerations = 8;

    // Dispatch PRE-save so integration-event publishes that fan out from
    // domain handlers participate in the same SaveChanges scope as the
    // aggregate write. MassTransit's bus-wide outbox stages OutboxMessage
    // rows on the shared PamMessagingDbContext when bridge handlers call
    // IPublishEndpoint.Publish; OutboxFlushBehavior commits those at the
    // tail of the command pipeline (a SEPARATE transaction from the
    // business save — see DECISIONS.md ADR #28 for why, and the
    // OutboxReconciliationService that closes the under-deliver gap).
    //
    // Trade-off: handlers see pre-commit state of the business DbContext.
    // If a handler throws, the business SaveChanges fails — but the
    // messaging SaveChanges has not run yet, so no outbox row is written
    // either. Net effect: aggregate write + outbox queue are either both
    // attempted or both skipped from the producer's side.
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        if (eventData.Context is not null)
        {
            await DispatchAsync(eventData.Context, cancellationToken);
        }
        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private async Task DispatchAsync(DbContext context, CancellationToken ct)
    {
        for (var generation = 0; generation < MaxGenerations; generation++)
        {
            var aggregates = context
                .ChangeTracker.Entries<IAggregate>()
                .Where(e => e.Entity.DomainEvents.Count > 0)
                .Select(e => e.Entity)
                .ToList();

            if (aggregates.Count == 0)
            {
                return;
            }

            foreach (var aggregate in aggregates)
            {
                var events = aggregate.ClearDomainEvents();
                foreach (var domainEvent in events)
                {
                    var notificationType = typeof(DomainEventNotification<>).MakeGenericType(
                        domainEvent.GetType()
                    );
                    var notification = (INotification)
                        Activator.CreateInstance(notificationType, domainEvent)!;
                    await publisher.Publish(notification, ct);
                }
            }
        }

        // If we got here, handlers kept raising events. That's almost
        // certainly a bug — call it out instead of silently looping.
        throw new InvalidOperationException(
            $"Domain-event dispatch exceeded {MaxGenerations} generations. "
                + "A handler is repeatedly raising new events on tracked aggregates."
        );
    }
}
