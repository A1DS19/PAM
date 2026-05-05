namespace Pam.Shared.Contracts.DDD;

public interface IDomainEvent
{
    Guid EventId { get; }

    DateTimeOffset OccurredAt { get; }
}
