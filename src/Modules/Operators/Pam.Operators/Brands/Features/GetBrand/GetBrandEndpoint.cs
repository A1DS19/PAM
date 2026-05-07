using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pam.Operators.Contracts.Brands.Dtos;
using Pam.Operators.Contracts.Brands.Queries;

namespace Pam.Operators.Brands.Features.GetBrand;

public sealed class GetBrandEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(
                "/v1/operators/brands/{id:guid}",
                async (Guid id, ISender sender, CancellationToken ct) =>
                {
                    var brand = await sender.Send(new GetBrandByIdQuery(id), ct);
                    return Results.Ok(brand);
                }
            )
            .WithTags("Operators")
            .WithName("GetBrand")
            .Produces<BrandDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .AllowAnonymous();
    }
}
