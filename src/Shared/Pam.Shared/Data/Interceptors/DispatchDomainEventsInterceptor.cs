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

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default
    )
    {
        if (eventData.Context is not null)
        {
            await DispatchAsync(eventData.Context, cancellationToken);
        }
        return await base.SavedChangesAsync(eventData, result, cancellationToken);
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
