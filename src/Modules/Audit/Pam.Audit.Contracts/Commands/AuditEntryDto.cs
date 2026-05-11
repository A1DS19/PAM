using Pam.Shared.Contracts.Audit;
using Pam.Shared.Contracts.Identity;

namespace Pam.Audit.Contracts.Commands;

public sealed record AuditEntryDto(
    Guid Id,
    string? CorrelationId,
    ActorType ActorType,
    string ActorId,
    string RequestType,
    string PayloadJson,
    DateTimeOffset StartedAt,
    int DurationMs,
    AuditStatus Status,
    string? ErrorType,
    string? ErrorMessage
);
