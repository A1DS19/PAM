using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Pam.Identity.Authentication.ConfirmEmail;

// POST /v1/identity/confirm-email { email, token }
// Anonymous — the user clicking the link from email may not be signed in.
public sealed class ConfirmEmailEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(
                "/v1/identity/confirm-email",
                async (ConfirmEmailCommand command, ISender sender, CancellationToken ct) =>
                {
                    await sender.Send(command, ct);
                    return Results.NoContent();
                }
            )
            .AllowAnonymous()
            .RequireRateLimiting("auth-sensitive")
            .WithTags("Identity")
            .WithName("ConfirmEmail")
            .WithSummary("Confirm a user's email address")
            .WithDescription(
                """
                Completes the email-confirmation flow that follows
                `CreateUser` or `SendConfirmationEmail`. Verifies the `token`
                against the Identity data-protection store and flips
                `EmailConfirmed` to `true` for the matching user.

                **Auth:** anonymous — the user clicking the link from their
                email may not be signed in. Rate-limited by the
                `auth-sensitive` policy.

                **Idempotency:** not idempotent — the token is consumed on
                success. A second POST with the same token returns
                `422 Unprocessable Entity`.

                **Side effects:** sets `EmailConfirmed = true`, which enables
                interactive sign-in when `RequireConfirmedEmail` is on.

                **Status codes:**
                - `204 No Content` — email confirmed.
                - `400 Bad Request` — malformed body.
                - `422 Unprocessable Entity` — invalid or expired token, or
                  email does not match a known user.
                - `429 Too Many Requests` — rate-limited.
                """
            )
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }
}
