using MediatR;
using Pam.Shared.Contracts.DDD;

namespace Pam.Shared.DDD;

public sealed record DomainEventNotification<TEvent>(TEvent Event) : INotification
    where TEvent : IDomainEvent;
