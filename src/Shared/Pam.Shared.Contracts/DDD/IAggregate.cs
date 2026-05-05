namespace Pam.Shared.Contracts.DDD;

public interface IAggregate : IEntity
{
    IReadOnlyList<IDomainEvent> DomainEvents { get; }

    IDomainEvent[] ClearDomainEvents();
}

public interface IAggregate<out TId> : IAggregate, IEntity<TId>;
