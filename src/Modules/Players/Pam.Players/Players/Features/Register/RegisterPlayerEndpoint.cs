using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Pam.Players.Players.Features.Register;

public sealed class RegisterPlayerEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(
                "/v1/auth/register",
                async (RegisterPlayer cmd, ISender sender, CancellationToken ct) =>
                {
                    var id = await sender.Send(cmd, ct);
                    return Results.Created($"/v1/players/{id}", new { id });
                }
            )
            .AllowAnonymous()
            .RequireRateLimiting("auth-sensitive")
            .WithTags("Players")
            .WithName("RegisterPlayer")
            .Produces(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);
    }
}
