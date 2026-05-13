using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pam.Identity.Contracts.Permissions;

namespace Pam.Identity.Users.Features.UnlockUser;

public sealed class UnlockUserEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(
                "/v1/identity/users/{id:guid}/unlock",
                async (Guid id, ISender sender, CancellationToken ct) =>
                {
                    await sender.Send(new UnlockUserCommand(id), ct);
                    return Results.NoContent();
                }
            )
            .WithTags("Identity")
            .WithName("UnlockUser")
            .WithSummary("Clear a user's lockout state")
            .WithDescription(
                """
                Resets the failed-access counter and clears the lockout end
                date for the target user, allowing them to sign in again
                without waiting for the lockout window to expire.

                **Auth:** requires `identity.users.write` permission.

                **Idempotency:** idempotent — calling on a user who is not
                locked out succeeds with `204 No Content`.

                **Side effects:** does NOT rotate the security stamp; existing
                sessions for the user (if any) remain valid.

                **Status codes:**
                - `204 No Content` — lockout cleared.
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
