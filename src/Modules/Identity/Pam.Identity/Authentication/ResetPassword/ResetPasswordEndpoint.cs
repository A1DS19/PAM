using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Pam.Identity.Authentication.ResetPassword;

// POST /v1/identity/reset-password { email, token, newPassword }
public sealed class ResetPasswordEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(
                "/v1/identity/reset-password",
                async (ResetPasswordCommand command, ISender sender, CancellationToken ct) =>
                {
                    await sender.Send(command, ct);
                    return Results.NoContent();
                }
            )
            .AllowAnonymous()
            .RequireRateLimiting("auth-sensitive")
            .WithTags("Identity")
            .WithName("ResetPassword")
            .WithSummary("Reset a password using an emailed token")
            .WithDescription(
                """
                Completes the password-reset flow started by
                `/v1/identity/forgot-password`. Verifies the `token` against the
                Identity data-protection store and sets the user's password to
                `newPassword` if the token is valid and unexpired.

                **Auth:** anonymous; rate-limited by the `auth-sensitive`
                policy.

                **Idempotency:** not idempotent — the token is consumed on
                success. A second POST with the same token returns
                `422 Unprocessable Entity`.

                **Side effects:** rotates the user's security stamp on success,
                which revokes all existing sessions and refresh tokens. Does
                NOT auto-sign-in the user; the SPA navigates to the login page.

                **Status codes:**
                - `204 No Content` — password reset succeeded.
                - `400 Bad Request` — malformed body.
                - `422 Unprocessable Entity` — invalid or expired token, or new
                  password violates Identity policy.
                - `429 Too Many Requests` — rate-limited.
                """
            )
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }
}
