using Pam.Shared.Contracts.DDD;

namespace Pam.Shared.DDD;

public abstract class Entity<TId> : IEntity<TId>
{
    public TId Id { get; protected set; } = default!;

    public DateTimeOffset CreatedAt { get; private set; }

    public string? CreatedBy { get; private set; }

    public DateTimeOffset? LastModifiedAt { get; private set; }

    public string? LastModifiedBy { get; private set; }
}
