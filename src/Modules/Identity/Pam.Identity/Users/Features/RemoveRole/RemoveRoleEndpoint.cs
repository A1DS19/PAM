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
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequireAuthorization($"Permissions.{PermissionCodes.Identity.RolesWrite}");
    }
}
