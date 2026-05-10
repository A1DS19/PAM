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
