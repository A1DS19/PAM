using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pam.Identity.Contracts.Permissions;

namespace Pam.Identity.Users.Features.DeleteUser;

public sealed class DeleteUserEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapDelete(
                "/v1/identity/users/{id:guid}",
                async (Guid id, ISender sender, CancellationToken ct) =>
                {
                    await sender.Send(new DeleteUserCommand(id), ct);
                    return Results.NoContent();
                }
            )
            .WithTags("Identity")
            .WithName("DeleteUser")
            .WithSummary("Delete a back-office user")
            .WithDescription(
                """
                Removes a back-office user from the Identity store. The
                operation is hard-delete by default; the audit trail in
                `audit.command_log` preserves the actor + payload for forensic
                review.

                **Auth:** requires `identity.users.write` permission.

                **Idempotency:** idempotent — a DELETE against an already-gone
                user returns `404 Not Found`, but the database state is the
                same whether you call it once or many times.

                **Side effects:** revokes all sessions and refresh tokens
                associated with the user.

                **Status codes:**
                - `204 No Content` — user deleted.
                - `401 Unauthorized` / `403 Forbidden` — auth failed or caller
                  lacks `identity.users.write`.
                - `404 Not Found` — no user with that id.
                - `422 Unprocessable Entity` — Identity refused (e.g. deleting
                  the last Owner is blocked).
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
