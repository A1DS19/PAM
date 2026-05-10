using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pam.Identity.Contracts.Permissions;

namespace Pam.Identity.Users.Features.UpdateUser;

public sealed class UpdateUserEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPatch(
                "/v1/identity/users/{id:guid}",
                async (Guid id, UpdateUserBody body, ISender sender, CancellationToken ct) =>
                {
                    await sender.Send(
                        new UpdateUserCommand(id, body.Email, body.BrandId, body.LockoutEnabled),
                        ct
                    );
                    return Results.NoContent();
                }
            )
            .WithTags("Identity")
            .WithName("UpdateUser")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequireAuthorization($"Permissions.{PermissionCodes.Identity.UsersWrite}");
    }
}

public sealed record UpdateUserBody(string? Email, Guid? BrandId, bool? LockoutEnabled);
