using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pam.Identity.Contracts.Permissions;

namespace Pam.Identity.Users.Features.RemoveRole;

public sealed class RemoveRoleEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapDelete(
                "/v1/identity/users/{id:guid}/roles/{role}",
                async (Guid id, string role, ISender sender, CancellationToken ct) =>
                {
                    await sender.Send(new RemoveRoleCommand(id, role), ct);
                    return Results.NoContent();
                }
            )
            .WithTags("Identity")
            .WithName("RemoveRole")
            .WithSummary("Remove a role from a user")
            .WithDescription(
                """
                Removes the named role from the target user. The role name is
                taken from the route.

                **Auth:** requires `identity.roles.write` permission.

                **Idempotency:** idempotent — removing a role the user does
                not have succeeds with `204 No Content`.

                **Side effects:** future requests from the user lose the
                permission claims granted by that role; existing sessions
                retain their claim set until refresh.

                **Status codes:**
                - `204 No Content` — role removed (or already absent).
                - `401 Unauthorized` / `403 Forbidden` — auth failed or caller
                  lacks `identity.roles.write`.
                - `404 Not Found` — no user with that id.
                - `422 Unprocessable Entity` — removal blocked by policy
                  (e.g. removing the last Owner role).
                """
            )
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequireAuthorization($"Permissions.{PermissionCodes.Identity.RolesWrite}");
    }
}
