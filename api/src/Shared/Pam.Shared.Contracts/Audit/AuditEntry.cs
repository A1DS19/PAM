using Pam.Shared.Contracts.Identity;

namespace Pam.Shared.Contracts.Audit;

public sealed record AuditEntry(
    string? CorrelationId,
    ActorType ActorType,
    string ActorId,
    string RequestType,
    string PayloadJson,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    AuditStatus Status,
    string? ErrorType,
    string? ErrorMessage
);
