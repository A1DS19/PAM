using Pam.Ingest.Contracts.Transactions.Models;

namespace Pam.Ingest.Vendors.TwentyOneG;

// Public request DTO — declared at the namespace level (not nested in the
// adapter) so the OpenAPI/Scalar generator picks it up. Real 21G is
// SOAP-shaped; this JSON form is the Phase-A intermediate the team uses
// to integration-test before the SOAP listener is wired.
public sealed record TwentyOneGRequest(
    Guid BrandId,
    Guid PlayerId,
    string Reference,
    long AmountCents,
    string Currency,
    TransactionKind Kind,
    DateTimeOffset OccurredAt,
    string? RoundId,
    string? Description
);

// Public response DTO. `Status` is a stringified TransactionStatus so the
// vendor sees `"Received" | "Duplicate" | "Rejected" | "Posted"` directly
// rather than enum ordinals.
public sealed record TwentyOneGResponse(
    Guid TransactionId,
    string Status,
    string? RejectedReason
);
