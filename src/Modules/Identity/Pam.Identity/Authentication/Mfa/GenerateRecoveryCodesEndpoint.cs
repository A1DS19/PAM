using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Pam.Identity.Authentication.Mfa;

// POST /v1/identity/me/mfa/recovery-codes
//   → { codes: [...] }   plaintext, returned once. Re-issuing invalidates
//     any previously-issued codes.
public sealed class GenerateRecoveryCodesEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(
                "/v1/identity/me/mfa/recovery-codes",
                async (ISender sender, CancellationToken ct) =>
                {
                    var result = await sender.Send(new GenerateRecoveryCodesCommand(), ct);
                    return Results.Ok(result);
                }
            )
            .WithTags("Identity")
            .WithName("GenerateRecoveryCodes")
            .WithSummary("Issue a fresh set of MFA recovery codes")
            .WithDescription(
                """
                Generates a new batch of one-time recovery codes for the
                signed-in user and returns them as plaintext in the response —
                this is the **only** time the codes are visible. Only the hashed
                form is persisted, so a lost code cannot be retrieved later;
                the user must call this endpoint again to get a new batch.

                **Auth:** any authenticated back-office user with MFA enabled.
                Rate-limited by the `auth-sensitive` policy.

                **Idempotency:** NOT idempotent in a destructive way — each
                call invalidates the previous batch in full. Retrying is safe
                in the sense that it can't corrupt state, but the previously
                returned codes will stop working. Callers should treat this as
                a "rotate" operation, not a "fetch".

                **Side effects:** replaces the user's recovery-code hashes in
                the Identity store; any code from a prior batch becomes
                permanently invalid.

                **Status codes:**
                - `200 OK` — `{ codes: [...] }` returned; show once, never
                  again.
                - `401 Unauthorized` — not signed in.
                - `422 Unprocessable Entity` — MFA is not enabled on the
                  account (enroll + verify first).
                - `429 Too Many Requests` — rate-limited.
                """
            )
            .Produces<GenerateRecoveryCodesResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequireAuthorization()
            .RequireRateLimiting("auth-sensitive");
    }
}
