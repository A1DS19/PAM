using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pam.Identity.Contracts.Permissions;

namespace Pam.Identity.Users.Features.ListUsers;

// GET /v1/identity/users?page=1&pageSize=50&brandId=...&role=Owner&lockedOut=true
public sealed class ListUsersEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(
                "/v1/identity/users",
                async (
                    ISender sender,
                    CancellationToken ct,
                    int page = 1,
                    int pageSize = 50,
                    Guid? brandId = null,
                    string? role = null,
                    bool? lockedOut = null
                ) =>
                {
                    var result = await sender.Send(
                        new ListUsersQuery(page, pageSize, brandId, role, lockedOut),
                        ct
                    );
                    return Results.Ok(result);
                }
            )
            .WithTags("Identity")
            .WithName("ListUsers")
            .WithSummary("List back-office users with filtering and paging")
            .WithDescription(
                """
                Returns a paged list of back-office users. Supports filtering by
                brand, role, and lockout state via query parameters. Results
                include total count for client-side pagination controls.

                **Request:** query string — `page` (default 1), `pageSize`
                (default 50), optional `brandId`, `role`, `lockedOut`.

                **Auth:** requires `identity.users.read` permission.

                **Status codes:**
                - `200 OK` — `ListUsersResult` with `items[]` and `total`.
                - `400 Bad Request` — invalid query parameter (e.g. `pageSize`
                  out of range).
                - `401 Unauthorized` / `403 Forbidden` — auth failed or caller
                  lacks `identity.users.read`.
                """
            )
            .Produces<ListUsersResult>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireAuthorization($"Permissions.{PermissionCodes.Identity.UsersRead}");
    }
}
