namespace Pam.Shared.Contracts.DDD;

public interface IEntity
{
    DateTimeOffset CreatedAt { get; }

    string? CreatedBy { get; }

    DateTimeOffset? LastModifiedAt { get; }

    string? LastModifiedBy { get; }
}

public interface IEntity<out TId> : IEntity
{
    TId Id { get; }
}
