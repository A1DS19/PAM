using Microsoft.Extensions.Logging;
using Pam.Audit.Data;
using Pam.Audit.Models;
using Pam.Shared.Contracts.Audit;

namespace Pam.Audit.Services;

public sealed class AuditService(AuditDbContext db, ILogger<AuditService> logger) : IAuditService
{
    public async Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        try
        {
            var row = AuditCommandLog.From(Guid.CreateVersion7(), entry);
            db.CommandLog.Add(row);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Audit failure must never fail the command — matches the
            // CachingBehavior pattern. Losing the occasional audit row to
            // a SQL Server blip is the lesser evil compared to making the
            // audit DB a single point of failure for /v1/* writes.
            logger.LogWarning(
                ex,
                "Failed to record audit entry for {RequestType} by {ActorType}:{ActorId}",
                entry.RequestType,
                entry.ActorType,
                entry.ActorId
            );
        }
    }
}
