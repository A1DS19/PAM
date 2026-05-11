using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pam.Identity.Contracts.Permissions;
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
            .WithSummary("Get a brand by id")
            .WithDescription(
                """
                Returns the full `BrandDto` (id, name, slug, jurisdiction,
                timestamps) for the brand identified by the route id.

                **Auth:** requires `operators.brands.read` permission.

                **Status codes:**
                - `200 OK` — brand payload returned.
                - `401 Unauthorized` / `403 Forbidden` — auth failed or caller
                  lacks `operators.brands.read`.
                - `404 Not Found` — no brand exists with that id.
                """
            )
            .Produces<BrandDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization($"Permissions.{PermissionCodes.Operators.BrandsRead}");
    }
}
