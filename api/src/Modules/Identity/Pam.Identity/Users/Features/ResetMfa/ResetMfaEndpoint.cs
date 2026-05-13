using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pam.Identity.Contracts.Permissions;

namespace Pam.Identity.Users.Features.ResetMfa;

public sealed class ResetMfaEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(
                "/v1/identity/users/{id:guid}/mfa/reset",
                async (Guid id, ISender sender, CancellationToken ct) =>
                {
                    await sender.Send(new ResetMfaCommand(id), ct);
                    return Results.NoContent();
                }
            )
            .WithTags("Identity")
            .WithName("ResetMfa")
            .WithSummary("Reset a user's MFA enrollment (admin override)")
            .WithDescription(
                """
                Administratively disables MFA for the target user — used when
                the user has lost both their authenticator app and their
                recovery codes. Clears the shared key, sets
                `TwoFactorEnabled = false`, and invalidates all outstanding
                recovery codes so the user can sign in with just their
                password and re-enroll from scratch.

                **Auth:** requires `identity.users.write` permission.

                **Idempotency:** idempotent — calling on a user who already
                has MFA disabled succeeds with `204 No Content`. The result
                state ("MFA disabled, no shared key, no recovery codes") is
                the same regardless of how many times it's called; the
                operation never re-enables anything.

                **Side effects:** rotates the user's security stamp, which
                revokes all existing sessions. Audited in `audit.command_log`
                — this is a sensitive override and a forensic trail matters.

                **Status codes:**
                - `204 No Content` — MFA reset.
                - `401 Unauthorized` / `403 Forbidden` — auth failed or caller
                  lacks `identity.users.write`.
                - `404 Not Found` — no user with that id.
                - `422 Unprocessable Entity` — Identity refused the change.
                """
            )
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequireAuthorization($"Permissions.{PermissionCodes.Identity.UsersWrite}");
    }
}
