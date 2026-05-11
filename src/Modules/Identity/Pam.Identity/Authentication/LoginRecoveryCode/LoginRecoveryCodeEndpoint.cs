using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Pam.Identity.Authentication.LoginRecoveryCode;

// POST /v1/identity/login/recovery-code { code }
//   204 success — auth cookie set, code consumed (one-time use)
//   401 invalid code or no partial cookie (ValidationProblemDetails with `errors`)
//   423 locked out
public sealed class LoginRecoveryCodeEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(
                "/v1/identity/login/recovery-code",
                async (LoginRecoveryCodeCommand command, ISender sender, CancellationToken ct) =>
                {
                    await sender.Send(command, ct);
                    return Results.NoContent();
                }
            )
            .AllowAnonymous()
            .RequireRateLimiting("auth-sensitive")
            .WithTags("Identity")
            .WithName("LoginRecoveryCode")
            .WithSummary("Complete sign-in with a one-time recovery code")
            .WithDescription(
                """
                Alternative second factor for users who have lost access to their
                authenticator app. Consumes one of the MFA recovery codes issued
                at enrollment (or via `/v1/identity/me/mfa/recovery-codes`) and
                upgrades the partial auth cookie to a full one.

                **Auth:** anonymous (carries the partial auth cookie);
                rate-limited by the `auth-sensitive` policy.

                **Idempotency:** not idempotent — each recovery code is
                single-use. A second POST with the same code returns `401`.

                **Side effects:** marks the code as consumed in the Identity
                store; issues the full auth cookie on success.

                **Status codes:**
                - `204 No Content` — code accepted; auth cookie upgraded.
                - `400 Bad Request` — malformed body.
                - `401 Unauthorized` — invalid code, already-used code, or no
                  partial cookie (`ValidationProblemDetails` with `errors`).
                - `423 Locked` — account locked.
                - `429 Too Many Requests` — rate-limited.
                """
            )
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status423Locked)
            .ProducesValidationProblem();
    }
}
