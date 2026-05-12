using MediatR;
using Pam.Ingest.Contracts.Transactions.Models;
using Pam.Ingest.Contracts.Vendors;
using Pam.Ingest.Transactions.Features.Ingest;
using Pam.Shared.Time;

namespace Pam.Ingest.Vendors.TwentyOneG.Soap;

// Phase-A implementation of `CustomerTransaction21G.asmx` PostTransaction.
//
// Today: persists a canonical VendorTransaction row, returns a stub
// TransactionResult. Forwarding to GBS lands in a follow-up PR via
// IGbsRelay — at which point DocumentNumber, RespMessage, and
// AvailableBalance get populated from GBS's response verbatim.
//
// Until that forwarder exists, this listener is NOT yet a drop-in for
// 21G traffic — flipping their config would mean 21G sees `DocumentNumber:
// 0` and won't be able to reconcile against GBS. Build phase is "running
// SoapCore service that compiles + persists." Production flip waits for
// IGbsRelay.
public sealed class TwentyOneGCustomerTransactionService(ISender sender, IClock clock)
    : ITwentyOneGCustomerTransactionService
{
    public async Task<TransactionResult> PostTransaction(
        string? systemID,
        string? systemPassword,
        string? clerkID,
        string? customerID,
        string? amount,
        string? tranCode,
        string? tranType,
        string? description,
        string? bettingAdjustmentFlagYN,
        string? dailyFigureDate_YYYYMMDD,
        string? enteredBy,
        string? paymentBy
    )
    {
        // Phase-A stub auth — accept any non-empty systemID. Real impl
        // validates (systemID, systemPassword) against ingest.vendor_credentials.
        if (string.IsNullOrEmpty(systemID))
        {
            return new TransactionResult { RespMessage = "Missing systemID." };
        }

        var amountCents = TwentyOneGReferenceHasher.ParseAmountToCents(amount);
        if (amountCents is null)
        {
            return new TransactionResult { RespMessage = $"Invalid amount '{amount}'." };
        }

        // 21G uses tranCode = 'D' (debit/Risk) / 'C' (credit/Win). Map to
        // our canonical TransactionKind. Defensive default: treat unknown
        // codes as Correction so they surface in audit rather than getting
        // silently lost.
        var kind = (tranCode ?? string.Empty) switch
        {
            "D" => TransactionKind.Risk,
            "C" => TransactionKind.Win,
            _ => TransactionKind.Correction,
        };

        var signedCents =
            kind == TransactionKind.Risk
                ? -Math.Abs(amountCents.Value)
                : Math.Abs(amountCents.Value);

        var vendorReference = TwentyOneGReferenceHasher.ComputeReference(
            systemID,
            customerID,
            dailyFigureDate_YYYYMMDD,
            amount,
            tranCode,
            tranType,
            description
        );

        // 21G's `dailyFigureDate_YYYYMMDD` is the business day, which is the
        // semantically correct OccurredAt. If absent or unparseable, fall
        // back to "now" via IClock so the row still lands somewhere on
        // the timeline rather than getting rejected at validation.
        var parsedDate = TwentyOneGReferenceHasher.ParseDailyFigureDate(dailyFigureDate_YYYYMMDD);
        var occurredAt = parsedDate is { } date
            ? new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc))
            : clock.UtcNow;

        // PlayerId is Guid.Empty in Phase A — Pam.Players module isn't
        // built yet, so we can't resolve the vendor's customerID to a
        // PAM player. The raw customerID is preserved in the description
        // field today; once a vendor_customer_id column lands we'll
        // promote it to a first-class indexed field.
        var command = new IngestTransactionCommand(
            VendorId: VendorCodes.TwentyOneG,
            VendorReference: vendorReference,
            BrandId: Guid.Empty,
            PlayerId: Guid.Empty,
            AmountCents: signedCents,
            Currency: "USD",
            Kind: kind,
            OccurredAt: occurredAt,
            RoundId: null,
            Description: $"customerID={customerID}; tranType={tranType}; description={description}"
        );

        var result = await sender.Send(command, CancellationToken.None);

        // Phase A: until IGbsRelay lands, DocumentNumber + AvailableBalance
        // are stubbed. RespMessage tells 21G we received it.
        return new TransactionResult
        {
            DocumentNumber = 0,
            RespMessage = result.Status switch
            {
                TransactionStatus.Received => "Accepted (PAM Phase A — not yet forwarded to GBS)",
                TransactionStatus.Duplicate => "Duplicate — ignored",
                TransactionStatus.Rejected => result.RejectedReason ?? "Rejected",
                _ => "OK",
            },
            AvailableBalance = 0,
        };
    }
}
