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

    // Inline audit stamp for hot paths that can skip the
    // AuditableSaveChangesInterceptor. Used by VendorTransaction —
    // 600+ inserts/sec at stress where per-save IUserContext resolution
    // and ChangeTracker reflection both showed up as meaningful overhead.
    // The interceptor still covers every other module's writes; opting
    // out is a per-aggregate decision, not a default.
    protected void Stamp(DateTimeOffset now, Actor actor)
    {
        CreatedAt = now;
        CreatedByType = actor.Type;
        CreatedById = actor.Id;
        LastModifiedAt = now;
        LastModifiedByType = actor.Type;
        LastModifiedById = actor.Id;
    }
}
