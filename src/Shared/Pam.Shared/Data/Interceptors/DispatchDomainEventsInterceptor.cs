using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Pam.Shared.Contracts.DDD;
using Pam.Shared.DDD;

namespace Pam.Shared.Data.Interceptors;

public sealed class DispatchDomainEventsInterceptor(IPublisher publisher) : SaveChangesInterceptor
{
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
        {
            await DispatchAsync(eventData.Context, cancellationToken);
        }
        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (eventData.Context is not null)
        {
            DispatchAsync(eventData.Context, CancellationToken.None).GetAwaiter().GetResult();
        }
        return base.SavingChanges(eventData, result);
    }

    private async Task DispatchAsync(DbContext context, CancellationToken ct)
    {
        while (true)
        {
            var aggregates = context.ChangeTracker
                .Entries<IAggregate>()
                .Where(e => e.Entity.DomainEvents.Count > 0)
                .Select(e => e.Entity)
                .ToList();

            if (aggregates.Count == 0)
            {
                break;
            }

            foreach (var aggregate in aggregates)
            {
                var events = aggregate.ClearDomainEvents();
                foreach (var domainEvent in events)
                {
                    var notificationType = typeof(DomainEventNotification<>)
                        .MakeGenericType(domainEvent.GetType());
                    var notification = (INotification)Activator.CreateInstance(
                        notificationType,
                        domainEvent)!;
                    await publisher.Publish(notification, ct);
                }
            }
        }
    }
}
