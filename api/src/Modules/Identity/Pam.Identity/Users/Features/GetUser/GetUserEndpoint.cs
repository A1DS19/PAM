using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pam.Identity.Contracts.Permissions;
using Pam.Identity.Contracts.Users.Dtos;

namespace Pam.Identity.Users.Features.GetUser;

public sealed class GetUserEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(
                "/v1/identity/users/{id:guid}",
                async (Guid id, ISender sender, CancellationToken ct) =>
                {
                    var user = await sender.Send(new GetUserQuery(id), ct);
                    return Results.Ok(user);
                }
            )
            .WithTags("Identity")
            .WithName("GetUser")
            .WithSummary("Get a back-office user by id")
            .WithDescription(
                """
                Returns the full `BackOfficeUserDto` (id, email, brand, roles,
                lockout state, MFA state, timestamps) for the user identified
                by the route id.

                **Auth:** requires `identity.users.read` permission.

                **Status codes:**
                - `200 OK` — user payload returned.
                - `401 Unauthorized` / `403 Forbidden` — auth failed or caller
                  lacks `identity.users.read`.
                - `404 Not Found` — no user exists with that id.
                """
            )
            .Produces<BackOfficeUserDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization($"Permissions.{PermissionCodes.Identity.UsersRead}");
    }
}
