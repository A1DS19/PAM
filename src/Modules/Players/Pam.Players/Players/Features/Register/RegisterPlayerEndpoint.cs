using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Pam.Players.Players.Brands;

namespace Pam.Players.Players.Features.Register;

public sealed class RegisterPlayerEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(
                "/v1/auth/register",
                async (
                    HttpRequest httpReq,
                    RegisterPlayerRequest body,
                    IOptions<BrandRegistryOptions> brandOpts,
                    ISender sender,
                    CancellationToken ct
                ) =>
                {
                    var brandId =
                        httpReq.Headers["X-Brand"].FirstOrDefault() ?? brandOpts.Value.Default;

                    var cmd = new RegisterPlayer(
                        BrandId: brandId,
                        Email: body.Email,
                        Password: body.Password,
                        FirstName: body.FirstName,
                        LastName: body.LastName,
                        DateOfBirth: body.DateOfBirth,
                        CountryCode: body.CountryCode,
                        Region: body.Region
                    );

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

public sealed record RegisterPlayerRequest(
    string Email,
    string Password,
    string FirstName,
    string LastName,
    DateOnly DateOfBirth,
    string CountryCode,
    string? Region
);
