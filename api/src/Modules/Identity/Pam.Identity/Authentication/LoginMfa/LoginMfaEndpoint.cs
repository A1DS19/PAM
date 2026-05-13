using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Pam.Identity.Authentication.LoginMfa;

// POST /v1/identity/login/mfa  { code, rememberMachine? }
//   204 success — auth cookie upgraded from partial to full
//   401 bad code (ValidationProblemDetails with `errors`)
//   423 locked out
public sealed class LoginMfaEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(
                "/v1/identity/login/mfa",
                async (LoginMfaBody body, ISender sender, CancellationToken ct) =>
                {
                    await sender.Send(new LoginMfaCommand(body.Code, body.RememberMachine), ct);
                    return Results.NoContent();
                }
            )
            .AllowAnonymous()
            .RequireRateLimiting("auth-sensitive")
            .WithTags("Identity")
            .WithName("LoginMfa")
            .WithSummary("Complete sign-in with an MFA code")
            .WithDescription(
                """
                Second step of the MFA login flow. Verifies the TOTP `code` from
                the user's authenticator app against the partial-auth cookie
                issued by `/v1/identity/login`. On success the partial cookie is
                upgraded to a full auth cookie; if `rememberMachine` is true an
                additional persistent "trusted machine" cookie is set so future
                logins on the same device skip the MFA step.

                **Auth:** anonymous (carries the partial auth cookie);
                rate-limited by the `auth-sensitive` policy.

                **Side effects:** issues the full auth cookie on success;
                increments the lockout counter on failure.

                **Status codes:**
                - `204 No Content` — code accepted, full auth cookie issued.
                - `400 Bad Request` — malformed request body.
                - `401 Unauthorized` — wrong code or missing partial cookie
                  (`ValidationProblemDetails` with `errors`).
                - `423 Locked` — account locked after repeated failures.
                - `429 Too Many Requests` — rate-limited.
                """
            )
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status423Locked)
            .ProducesValidationProblem();
    }
}

public sealed record LoginMfaBody([FromBody] string Code, bool RememberMachine = false);
