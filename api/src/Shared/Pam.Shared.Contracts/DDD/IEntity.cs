using Pam.Shared.Contracts.Identity;

namespace Pam.Shared.Contracts.DDD;

public interface IEntity
{
    DateTimeOffset CreatedAt { get; }

    ActorType CreatedByType { get; }

    string CreatedById { get; }

    DateTimeOffset? LastModifiedAt { get; }

    ActorType? LastModifiedByType { get; }

    string? LastModifiedById { get; }
}

public interface IEntity<out TId> : IEntity
{
    TId Id { get; }
}
