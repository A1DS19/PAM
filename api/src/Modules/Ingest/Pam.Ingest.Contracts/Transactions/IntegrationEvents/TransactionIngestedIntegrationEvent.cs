using Pam.Ingest.Contracts.Transactions.Models;
using Pam.Shared.Messaging.Events;

namespace Pam.Ingest.Contracts.Transactions.IntegrationEvents;

// Lean integration event — IDs and routing data only. Consumers that need
// richer data (player email for receipts, brand-specific template selection)
// query Pam.Ingest.Contracts.IQuery<GetTransactionByIdQuery> or look up the
// player via Pam.Players.Contracts.
//
// Amount is signed cents: negative for Risk (debit), positive for Win
// (credit). Currency is ISO 4217.
//
// TransactionOccurredAt is the vendor-reported event time; the base
// IntegrationEvent.OccurredAt is when this *event* was raised (which is
// roughly `now` when the row was inserted). They are deliberately
// distinct — vendor clock skew and forwarding delay can put them
// minutes apart.
public sealed record TransactionIngestedIntegrationEvent(
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
    DateTimeOffset TransactionOccurredAt
) : IntegrationEvent;
