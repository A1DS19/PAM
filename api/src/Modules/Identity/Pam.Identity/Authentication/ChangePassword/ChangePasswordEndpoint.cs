using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Pam.Identity.Authentication.ChangePassword;

// POST /v1/identity/me/change-password
// Authenticated user only — no permission required beyond being logged in.
public sealed class ChangePasswordEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(
                "/v1/identity/me/change-password",
                async (ChangePasswordCommand command, ISender sender, CancellationToken ct) =>
                {
                    await sender.Send(command, ct);
                    return Results.NoContent();
                }
            )
            .WithTags("Identity")
            .WithName("ChangePassword")
            .WithSummary("Change the current user's password")
            .WithDescription(
                """
                Lets the signed-in user rotate their own password by supplying
                their current password and a new one. The Identity password
                policy (length, complexity, history) is enforced server-side.

                **Auth:** any authenticated back-office user — no extra
                permission required. Rate-limited by the `auth-sensitive`
                policy.

                **Side effects:** rotates the user's security stamp, which
                invalidates all other sessions and refresh tokens on success.

                **Status codes:**
                - `204 No Content` — password changed; current session remains
                  valid, all other sessions revoked.
                - `400 Bad Request` — validation failure (missing fields).
                - `401 Unauthorized` — caller is not signed in or the supplied
                  current password is wrong.
                - `422 Unprocessable Entity` — new password violates Identity
                  policy (too short, reuses recent history, etc.).
                - `429 Too Many Requests` — rate-limited.
                """
            )
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequireAuthorization()
            .RequireRateLimiting("auth-sensitive");
    }
}
