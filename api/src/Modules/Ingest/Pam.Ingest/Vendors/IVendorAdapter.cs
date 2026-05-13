using Microsoft.AspNetCore.Http;
using Pam.Ingest.Transactions.Features.Ingest;

namespace Pam.Ingest.Vendors;

// One implementation per casino vendor. Owns:
//   - Auth (HMAC, bearer, IP allow-list, plaintext password, …)
//   - Request-body parsing (each vendor's payload shape differs)
//   - Player-id resolution (vendor's username → PAM PlayerId)
//   - Sign convention (some vendors send absolute values + a tran type
//     code; we must produce signed AmountCents in the command)
//
// The endpoint pattern (one per vendor, registered via Carter) is:
//   1. Call adapter.AuthenticateAsync(request) — if false, 401.
//   2. Call adapter.TranslateAsync(request) — produces IngestTransactionCommand.
//   3. Send the command via ISender (MediatR pipeline: validate → audit → handler).
//   4. Call adapter.FormatResponseAsync(result, request) — vendor-shaped reply.
public interface IVendorAdapter
{
    // Stable identifier — matches VendorCodes constants. Used to register
    // the adapter in DI and to set VendorId on the command.
    string VendorId { get; }

    // Verify the request actually came from this vendor. HMAC, bearer,
    // client cert, IP allow-list — owns the variety so the rest of the
    // pipeline doesn't see it.
    Task<bool> AuthenticateAsync(HttpRequest request, CancellationToken cancellationToken);

    // Parse the vendor's payload + resolve player id. Returns null on
    // unparseable requests; the endpoint maps to 400.
    Task<IngestTransactionCommand?> TranslateAsync(
        HttpRequest request,
        CancellationToken cancellationToken
    );

    // Format the vendor-shaped response from the canonical result.
    // Different vendors expect different keys, sometimes different
    // envelope shapes (SOAP for 21G, JSON for newer ones).
    Task<IResult> FormatResponseAsync(
        IngestTransactionResult result,
        HttpRequest request,
        CancellationToken cancellationToken
    );
}
