using Microsoft.AspNetCore.Http;
using Pam.Ingest.Contracts.Transactions.Models;
using Pam.Ingest.Contracts.Vendors;
using Pam.Ingest.Transactions.Features.Ingest;

namespace Pam.Ingest.Vendors.TwentyOneG;

// Skeleton adapter for 21G Casino. In GBS this vendor uses SOAP with
// (systemId, systemPassword) credentials in the envelope. The skeleton
// here accepts a simplified JSON shape for the demo — wiring the real
// SOAP listener + the GBS-style credential check is Phase A work.
//
// What this stub does:
//   - Auth: bypass (returns true). Real impl: validate systemId +
//     systemPassword against a credential store keyed by VendorId.
//   - Translate: read a JSON body shaped like { customerId, amount,
//     reference, currency, kind, occurredAt } and produce an
//     IngestTransactionCommand. Real impl: parse the SOAP envelope,
//     resolve customerId via IPlayerLookup from Pam.Players.Contracts.
//   - Format: minimal JSON ack. Real impl: build the SOAP response
//     envelope GBS used to return.
public sealed class TwentyOneGAdapter : IVendorAdapter
{
    public string VendorId => VendorCodes.TwentyOneG;

    public Task<bool> AuthenticateAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        // Phase-A stub. Real implementation validates (systemId,
        // systemPassword) headers against a VendorCredentials table
        // seeded per environment. For the demo, require any non-empty
        // X-Vendor-System-Id header.
        var hasSystemId = !string.IsNullOrEmpty(request.Headers["X-Vendor-System-Id"]);
        return Task.FromResult(hasSystemId);
    }

    public async Task<IngestTransactionCommand?> TranslateAsync(
        HttpRequest request,
        CancellationToken cancellationToken
    )
    {
        var body = await request.ReadFromJsonAsync<TwentyOneGRequest>(cancellationToken);
        if (body is null)
        {
            return null;
        }

        // Phase-A stub. Pam.Players.Contracts.IPlayerLookup will
        // eventually resolve (BrandId, vendor-username) → PlayerId.
        // For the demo, the caller supplies the resolved PlayerId
        // directly.

        return new IngestTransactionCommand(
            VendorId: VendorId,
            VendorReference: body.Reference,
            BrandId: body.BrandId,
            PlayerId: body.PlayerId,
            // 21G uses positive amounts + an explicit tran code; we
            // normalize to signed cents at this seam. Risk → debit
            // (negative), Win/Bonus/Refund/Correction → credit (positive).
            AmountCents: body.Kind == TransactionKind.Risk
                ? -Math.Abs(body.AmountCents)
                : Math.Abs(body.AmountCents),
            Currency: body.Currency,
            Kind: body.Kind,
            OccurredAt: body.OccurredAt,
            RoundId: body.RoundId,
            Description: body.Description
        );
    }

    public Task<IResult> FormatResponseAsync(
        IngestTransactionResult result,
        HttpRequest request,
        CancellationToken cancellationToken
    )
    {
        // Stringify the status enum so the vendor sees a stable label
        // ("Received" | "Duplicate" | "Rejected" | "Posted") instead of
        // an ordinal. Matches what `.Produces<TwentyOneGResponse>()`
        // declares on the endpoint.
        var response = new TwentyOneGResponse(
            TransactionId: result.TransactionId,
            Status: result.Status.ToString(),
            RejectedReason: result.RejectedReason
        );

        return Task.FromResult(Results.Ok(response));
    }
}
