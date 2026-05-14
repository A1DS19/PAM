using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pam.Ingest.Contracts.Transactions.Models;
using Pam.Ingest.Contracts.Vendors;

namespace Pam.Ingest.Vendors.FastSpin;

// Phase-A intercept endpoint for Kingdom Casino (FastSpin). The CTO
// decision (2026-05-13): PAM sits transparently between FastSpin and
// GBS, captures BOTH directions, and relays GBS's response verbatim.
//
// Flow:
//   1. Buffer the inbound body so we can both forward and parse.
//   2. Forward bytes + headers to GBS via IFastSpinUpstream.
//   3. If verb is `transfer`, parse both bodies and persist a row in
//      ingest.vendor_transactions with downstream_* fields populated.
//   4. Relay GBS's exact response bytes + Content-Type + status to
//      FastSpin. No re-encode — the body's Digest signature must remain
//      bit-identical or FastSpin will reject the response.
//
// `getBalance` is a query, not a money movement — forward + relay, no row.
// `getAuthorize` is operator → casino (outbound from GBS), not handled here.
public sealed class FastSpinInterceptEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(
                $"/v1/ingest/vendors/{VendorCodes.FastSpin}/main",
                async (
                    HttpContext context,
                    IFastSpinUpstream upstream,
                    ISender sender,
                    CancellationToken ct
                ) =>
                {
                    // Buffer + read the inbound body once. We pass the
                    // bytes both to the upstream forward (byte-for-byte
                    // preservation keeps GBS's Digest validation happy)
                    // and to the adapter for parsing.
                    context.Request.EnableBuffering();
                    var requestBody = await ReadBodyAsync(context.Request, ct);

                    var result = await upstream.ForwardAsync(context.Request, requestBody, ct);

                    // Persist only for `transfer` verb. getBalance carries no
                    // transaction. The API header is FastSpin's verb dispatch.
                    var apiVerb = context.Request.Headers["API"].ToString();
                    if (
                        string.Equals(apiVerb, "transfer", StringComparison.OrdinalIgnoreCase)
                        && result.Outcome != DownstreamStatus.UpstreamUnreachable
                    )
                    {
                        var cmd = FastSpinAdapter.ExtractCommand(
                            requestBody.Span,
                            result.Body.Span,
                            result.Outcome,
                            result.LatencyMs
                        );
                        if (cmd is not null)
                        {
                            // Persist BEFORE relaying. A PAM crash after
                            // the relay would lose the audit record;
                            // doing it this order means FastSpin sees a
                            // 500 if we can't commit, and retries with
                            // the same transferId (idempotent path
                            // catches the dupe on the second attempt).
                            await sender.Send(cmd, ct);
                        }
                    }

                    // Relay GBS's exact response. Bytes, not JSON object,
                    // because a re-encode would change whitespace + key
                    // ordering and break any consumer that signs the body.
                    // Results.Bytes has no status-code overload, so write
                    // the response directly.
                    context.Response.StatusCode = result.StatusCode;
                    context.Response.ContentType = result.ContentType;
                    await context.Response.Body.WriteAsync(result.Body, ct);
                    return Results.Empty;
                }
            )
            .AllowAnonymous()
            .RequireRateLimiting("api-default")
            .Accepts<FastSpinTransferRequest>("application/json")
            .Produces<FastSpinTransferResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("Ingest")
            .WithName("InterceptFastSpin")
            .WithSummary("Forward + capture a FastSpin (Kingdom Casino) wallet call")
            .WithDescription(
                """
                Transparent intercept for FastSpin's wallet callbacks. PAM
                forwards the inbound request to GBS unchanged, captures
                both directions, persists a canonical row in
                `ingest.vendor_transactions` (transfer verb only), and
                relays GBS's exact response bytes back to FastSpin.

                **Auth:** anonymous; GBS validates the `Digest` header
                upstream. PAM is intentionally transparent — re-signing
                or re-encoding would break the vendor's body signature.

                **Idempotency:** the inbound `transferId` is the
                idempotency key on PAM's side (UNIQUE index on
                `(vendor_id, vendor_reference)`). GBS independently dedupes
                on its own `tbcasinoplaytoday.Reference`.

                **Verb dispatch:** via the `API` HTTP header. `transfer`
                persists a row; `getBalance` forwards + relays without
                persistence.

                **Status codes:**
                - `200 OK` — relayed from GBS verbatim (body carries
                  vendor-level code/msg).
                - `429 Too Many Requests` — rate-limited by `api-default`.
                - `502 Bad Gateway` — GBS unreachable (DNS / TLS / refused).
                - `503 Service Unavailable` — GBS timed out within budget.
                """
            );
    }

    private static async Task<ReadOnlyMemory<byte>> ReadBodyAsync(
        HttpRequest request,
        CancellationToken ct
    )
    {
        using var ms = new MemoryStream();
        await request.Body.CopyToAsync(ms, ct);
        request.Body.Position = 0;
        return ms.ToArray();
    }
}
