using Pam.Ingest.Contracts.Transactions.Models;
using Pam.Shared.Contracts.CQRS;

namespace Pam.Ingest.Transactions.Features.Ingest;

// Canonical command shape — every vendor adapter normalizes its incoming
// payload into one of these. The adapter is responsible for resolving the
// vendor's user identifier to a PAM PlayerId before constructing the
// command.
//
// AmountCents semantics: signed. Risk (debit) = negative; Win (credit)
// = positive. The handler does NOT flip the sign based on Kind — the
// adapter must produce the correct signed value.
//
// IUnauditedCommand: skips AuditBehavior. ingest.vendor_transactions is
// the audit trail for this command — actor, payload, timing, and status
// all live on the business row. Writing a 1:1 audit.command_log row at
// vendor-ingest volume (millions/day) bloats audit storage with no new
// information. Failures still land in LoggingBehavior + OTel.
public sealed record IngestTransactionCommand(
    string VendorId,
    string VendorReference,
    Guid BrandId,
    Guid PlayerId,
    long AmountCents,
    string Currency,
    TransactionKind Kind,
    DateTimeOffset OccurredAt,
    string? RoundId = null,
    string? Description = null
) : ICommand<IngestTransactionResult>, IUnauditedCommand;

// Returned to the adapter so it can craft the vendor-shaped response.
// The vendor wants to know: "did you accept this?" + a stable id it can
// use to query back later. Status discriminates between fresh insert,
// idempotent retry, and rejection.
public sealed record IngestTransactionResult(
    Guid TransactionId,
    TransactionStatus Status,
    string? RejectedReason = null
);
