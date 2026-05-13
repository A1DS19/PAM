using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Pam.Identity.Authentication.Mfa;

// POST /v1/identity/me/mfa/disable { currentPassword }
public sealed class MfaDisableEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(
                "/v1/identity/me/mfa/disable",
                async (MfaDisableCommand command, ISender sender, CancellationToken ct) =>
                {
                    await sender.Send(command, ct);
                    return Results.NoContent();
                }
            )
            .WithTags("Identity")
            .WithName("MfaDisable")
            .WithSummary("Disable MFA for the current user")
            .WithDescription(
                """
                Turns off two-factor authentication for the signed-in user.
                Requires the user's current password as a step-up check so a
                stolen session cookie alone can't weaken account security.

                **Auth:** any authenticated back-office user. Rate-limited by
                the `auth-sensitive` policy.

                **Idempotency:** safe to retry — calling on an account that
                already has MFA disabled is a successful no-op (still requires
                a valid current password).

                **Side effects:** sets `TwoFactorEnabled = false`, clears the
                shared key, and invalidates all existing recovery codes.

                **Status codes:**
                - `204 No Content` — MFA disabled.
                - `400 Bad Request` — malformed body.
                - `401 Unauthorized` — not signed in or wrong current password.
                - `422 Unprocessable Entity` — Identity refused the change
                  (policy violation).
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
