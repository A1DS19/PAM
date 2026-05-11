using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pam.Identity.Contracts.Permissions;

namespace Pam.Identity.Users.Features.AssignRole;

public sealed class AssignRoleEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(
                "/v1/identity/users/{id:guid}/roles",
                async (Guid id, AssignRoleBody body, ISender sender, CancellationToken ct) =>
                {
                    await sender.Send(new AssignRoleCommand(id, body.Role), ct);
                    return Results.NoContent();
                }
            )
            .WithTags("Identity")
            .WithName("AssignRole")
            .WithSummary("Assign a role to a user")
            .WithDescription(
                """
                Adds the named role to the target user. Roles are looked up by
                name; unknown roles return `422`.

                **Request:** body — `{ role }` (the role name, e.g.
                `"Manager"`).

                **Auth:** requires `identity.roles.write` permission.

                **Idempotency:** idempotent — assigning a role the user
                already has succeeds with `204 No Content`. The underlying
                Identity API treats it as a no-op.

                **Side effects:** the next request from this user picks up
                the new role and any permission claims it carries; existing
                sessions retain their old claim set until refresh.

                **Status codes:**
                - `204 No Content` — role assigned (or already present).
                - `400 Bad Request` — validation failure (empty role).
                - `401 Unauthorized` / `403 Forbidden` — auth failed or caller
                  lacks `identity.roles.write`.
                - `404 Not Found` — no user with that id.
                - `422 Unprocessable Entity` — role name is not defined.
                """
            )
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequireAuthorization($"Permissions.{PermissionCodes.Identity.RolesWrite}");
    }
}

public sealed record AssignRoleBody(string Role);
