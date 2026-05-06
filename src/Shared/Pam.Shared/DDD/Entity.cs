using Pam.Shared.Contracts.DDD;
using Pam.Shared.Contracts.Identity;

namespace Pam.Shared.DDD;

public abstract class Entity<TId> : IEntity<TId>
{
    public TId Id { get; protected set; } = default!;

    public DateTimeOffset CreatedAt { get; private set; }

    public ActorType CreatedByType { get; private set; }

    public string CreatedById { get; private set; } = default!;

    public DateTimeOffset? LastModifiedAt { get; private set; }

    public ActorType? LastModifiedByType { get; private set; }

    public string? LastModifiedById { get; private set; }
}
