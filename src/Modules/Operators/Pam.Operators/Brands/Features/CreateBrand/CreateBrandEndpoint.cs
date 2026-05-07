using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Pam.Operators.Brands.Features.CreateBrand;

public sealed class CreateBrandEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(
                "/v1/operators/brands",
                async (CreateBrandCommand command, ISender sender, CancellationToken ct) =>
                {
                    var id = await sender.Send(command, ct);
                    return Results.Created($"/v1/operators/brands/{id}", new { id });
                }
            )
            .WithTags("Operators")
            .WithName("CreateBrand")
            .Produces(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .AllowAnonymous();
    }
}
