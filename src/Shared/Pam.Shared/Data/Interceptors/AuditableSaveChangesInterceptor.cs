using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Pam.Shared.Contracts.DDD;
using Pam.Shared.Security;
using Pam.Shared.Time;

namespace Pam.Shared.Data.Interceptors;

public sealed class AuditableSaveChangesInterceptor(IClock clock, IUserContext userContext)
    : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
        {
            UpdateAuditableEntities(eventData.Context);
        }
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (eventData.Context is not null)
        {
            UpdateAuditableEntities(eventData.Context);
        }
        return base.SavingChanges(eventData, result);
    }

    private void UpdateAuditableEntities(DbContext context)
    {
        var now = clock.UtcNow;
        var actor = userContext.UserId;

        foreach (EntityEntry entry in context.ChangeTracker.Entries())
        {
            if (entry.Entity is not IEntity)
            {
                continue;
            }

            if (entry.State == EntityState.Added)
            {
                entry.Property(nameof(IEntity.CreatedAt)).CurrentValue = now;
                entry.Property(nameof(IEntity.CreatedBy)).CurrentValue = actor;
            }

            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Property(nameof(IEntity.LastModifiedAt)).CurrentValue = now;
                entry.Property(nameof(IEntity.LastModifiedBy)).CurrentValue = actor;
            }
        }
    }
}
