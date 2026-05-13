using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pam.Ingest.Contracts.Vendors;

namespace Pam.Ingest.Vendors.TwentyOneG;

// Vendor-specific entry point. The route pattern is
// /v1/ingest/vendors/<vendor-code>; one Carter module per vendor lives
// alongside its adapter. Anonymous + rate-limited — the adapter owns
// vendor auth (each vendor authenticates differently).
//
// The OpenAPI metadata (`Accepts`/`Produces`/`ProducesProblem`/summary/
// description) is what gives Scalar a rich card per vendor. The body is
// actually parsed inside the adapter via HttpContext.Request, but the
// `Accepts<TwentyOneGRequest>` hint tells OpenAPI the shape so the
// schema + example renders.
public sealed class TwentyOneGEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(
                $"/v1/ingest/vendors/{VendorCodes.TwentyOneG}",
                async (
                    HttpContext context,
                    TwentyOneGAdapter adapter,
                    ISender sender,
                    CancellationToken ct
                ) =>
                {
                    if (!await adapter.AuthenticateAsync(context.Request, ct))
                    {
                        return Results.Unauthorized();
                    }

                    var command = await adapter.TranslateAsync(context.Request, ct);
                    if (command is null)
                    {
                        return Results.BadRequest(new { error = "invalid_request_body" });
                    }

                    var result = await sender.Send(command, ct);
                    return await adapter.FormatResponseAsync(result, context.Request, ct);
                }
            )
            .AllowAnonymous()
            .RequireRateLimiting("api-default")
            .Accepts<TwentyOneGRequest>("application/json")
            .Produces<TwentyOneGResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesValidationProblem()
            .WithTags("Ingest")
            .WithName("Ingest21G")
            .WithSummary("Ingest a 21G Casino vendor transaction")
            .WithDescription(
                """
                Accepts a normalized JSON payload from 21G Casino and persists a
                canonical VendorTransaction row in `ingest.vendor_transactions`.

                **Auth:** vendor-side, via the `X-Vendor-System-Id` header in
                the Phase-A stub. Production will validate
                (systemId, systemPassword) against a credentials store.

                **Idempotency:** the `Reference` field is the idempotency key.
                Re-POST with the same Reference returns status `Duplicate`
                with the original transaction id; no second row is written.

                **Sign convention:** 21G submits positive amounts; the adapter
                negates the value when `Kind == Risk` (debit) and leaves it
                positive for Win / Refund / Bonus / Correction (credit).

                **Status codes:**
                - `200 OK` — accepted (status `Received`) OR idempotent
                  retry (status `Duplicate`).
                - `400 Bad Request` — unparseable body.
                - `401 Unauthorized` — vendor auth header missing/invalid.
                - `422 Unprocessable Entity` — validation failure
                  (unknown currency, future-dated by more than 24h, etc.).
                - `429 Too Many Requests` — rate-limited by the
                  `api-default` policy (100 req / 30s per partition).
                """
            );
    }
}
