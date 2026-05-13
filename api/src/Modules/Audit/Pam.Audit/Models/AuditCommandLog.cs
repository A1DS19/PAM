using Pam.Shared.Contracts.Audit;
using Pam.Shared.Contracts.Identity;

namespace Pam.Audit.Models;

// Immutable fact row — not an Aggregate. Audit entries are append-only,
// have no domain events, and never get mutated after they're written.
public sealed class AuditCommandLog
{
    public Guid Id { get; private set; }
    public string? CorrelationId { get; private set; }
    public ActorType ActorType { get; private set; }
    public string ActorId { get; private set; } = default!;
    public string RequestType { get; private set; } = default!;
    public string PayloadJson { get; private set; } = default!;
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset CompletedAt { get; private set; }
    public int DurationMs { get; private set; }
    public AuditStatus Status { get; private set; }
    public string? ErrorType { get; private set; }
    public string? ErrorMessage { get; private set; }

    private AuditCommandLog() { }

    public static AuditCommandLog From(Guid id, AuditEntry entry) =>
        new()
        {
            Id = id,
            CorrelationId = entry.CorrelationId,
            ActorType = entry.ActorType,
            ActorId = entry.ActorId,
            RequestType = entry.RequestType,
            PayloadJson = entry.PayloadJson,
            StartedAt = entry.StartedAt,
            CompletedAt = entry.CompletedAt,
            DurationMs = (int)(entry.CompletedAt - entry.StartedAt).TotalMilliseconds,
            Status = entry.Status,
            ErrorType = entry.ErrorType,
            ErrorMessage = entry.ErrorMessage,
        };
}
