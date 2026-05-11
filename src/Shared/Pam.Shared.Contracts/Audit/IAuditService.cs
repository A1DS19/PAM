namespace Pam.Shared.Contracts.Audit;

public interface IAuditService
{
    Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default);
}
