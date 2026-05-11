using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Pam.Identity.Authentication.Login;

// POST /v1/identity/login
//   Request:  { email, password, rememberMe? }
//   200      { mfaRequired: true }   when MFA is enrolled
//   204      success — auth cookie set, the SPA navigates to ?returnUrl
//   401      invalid credentials  (ValidationProblemDetails with `errors`)
//   423      account locked out
//
// All non-success outcomes throw from the handler and flow through
// CustomExceptionHandler so the ProblemDetails shape is identical to
// every other PAM error. Anonymous + auth-sensitive rate-limited (5/min/IP).
public sealed class LoginEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(
                "/v1/identity/login",
                async (LoginRequest body, ISender sender, CancellationToken ct) =>
                {
                    var result = await sender.Send(
                        new LoginCommand(body.Email, body.Password, body.RememberMe),
                        ct
                    );

                    return result.RequiresTwoFactor
                        ? Results.Ok(new { mfaRequired = true })
                        : Results.NoContent();
                }
            )
            .AllowAnonymous()
            .RequireRateLimiting("auth-sensitive")
            .WithTags("Identity")
            .WithName("Login")
            .WithSummary("Sign in with email and password")
            .WithDescription(
                """
                Authenticates a back-office user with email + password. If MFA is
                enrolled, returns `200 OK` with `{ mfaRequired: true }` and sets a
                partial auth cookie; the SPA then POSTs the second factor to
                `/v1/identity/login/mfa`. Otherwise sets the full auth cookie and
                returns `204 No Content`.

                All non-success outcomes throw from the handler and flow through
                `CustomExceptionHandler` so the `ProblemDetails` shape is
                identical to every other PAM error.

                **Auth:** anonymous; rate-limited by the `auth-sensitive` policy
                (5 requests / minute / IP).

                **Side effects:** issues an auth cookie on success; increments the
                Identity lockout counter on failure.

                **Status codes:**
                - `200 OK` — credentials valid, second factor required
                  (`{ mfaRequired: true }`).
                - `204 No Content` — fully signed in; auth cookie set.
                - `400 Bad Request` — malformed request body.
                - `401 Unauthorized` — invalid credentials
                  (`ValidationProblemDetails` with `errors`).
                - `423 Locked` — account is locked out after repeated failures.
                - `429 Too Many Requests` — rate-limited.
                """
            )
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status423Locked)
            .ProducesValidationProblem();
    }
}

public sealed record LoginRequest(
    [FromBody] string Email,
    string Password,
    bool RememberMe = false
);
