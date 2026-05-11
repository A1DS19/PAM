using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Pam.Identity.Authentication.ForgotPassword;

// POST /v1/identity/forgot-password { email }
//   → 204 always (anti-enumeration). Email is sent only when the user
//     exists and has a confirmed address.
public sealed class ForgotPasswordEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(
                "/v1/identity/forgot-password",
                async (ForgotPasswordCommand command, ISender sender, CancellationToken ct) =>
                {
                    await sender.Send(command, ct);
                    return Results.NoContent();
                }
            )
            .AllowAnonymous()
            .RequireRateLimiting("auth-sensitive")
            .WithTags("Identity")
            .WithName("ForgotPassword")
            .WithSummary("Request a password-reset email")
            .WithDescription(
                """
                Kicks off the password-reset flow for the email in the body.
                Always returns `204 No Content` regardless of whether the email
                matches a real user — this prevents account enumeration via
                response timing or status differences.

                **Auth:** anonymous; rate-limited by the `auth-sensitive`
                policy.

                **Idempotency:** safe to retry — generating a new token
                invalidates any previously-issued reset token for the same user.

                **Side effects:** when the email maps to a user with a confirmed
                address, sends a reset link via Notifications. Otherwise no
                email is sent but the response is identical.

                **Status codes:**
                - `204 No Content` — request accepted (whether or not an email
                  was actually dispatched).
                - `400 Bad Request` — malformed request body.
                - `429 Too Many Requests` — rate-limited.
                """
            )
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem();
    }
}
