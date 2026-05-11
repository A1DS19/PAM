using Pam.Ingest.Contracts.Transactions.Models;
using Pam.Shared.Contracts.DDD;
using Pam.Shared.Contracts.Identity;

namespace Pam.Ingest.Transactions.Events;

// In-module fact, dispatched in-process by DispatchDomainEventsInterceptor.
// The bridge handler (TransactionIngestedDomainHandler) translates this
// into the public IntegrationEvent that consumers in other modules see.
public sealed record TransactionIngestedDomainEvent(
    Guid TransactionId,
    string VendorId,
    string VendorReference,
    Guid BrandId,
    Guid PlayerId,
    long AmountCents,
    string Currency,
    TransactionKind Kind,
    TransactionStatus Status,
    string? RoundId,
    DateTimeOffset OccurredAt
) : IDomainEvent
{
    public Guid EventId { get; init; } = PamIds.New();

    public DateTimeOffset OccurredAtTimestamp { get; init; } = DateTimeOffset.UtcNow;

    // IDomainEvent.OccurredAt — the time the *event* was raised (right
    // now). Distinct from OccurredAt above which is the vendor-reported
    // time of the underlying business transaction.
    DateTimeOffset IDomainEvent.OccurredAt => OccurredAtTimestamp;
}
