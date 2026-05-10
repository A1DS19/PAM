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
            .Produces<ListUsersResult>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireAuthorization($"Permissions.{PermissionCodes.Identity.UsersRead}");
    }
}
