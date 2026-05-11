using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pam.Identity.Contracts.Permissions;

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
            .WithSummary("Create an operator brand")
            .WithDescription(
                """
                Creates a new operator brand record (name, slug, jurisdiction). The
                slug is the public, URL-safe handle and must be unique across the
                tenant — a Postgres UNIQUE constraint backs the uniqueness check.

                **Auth:** requires `operators.brands.write` permission.

                **Idempotency:** not idempotent — re-POST with the same slug
                returns `409 Conflict` (mapped from SQLSTATE `23505`).

                **Side effects:** raises a `BrandCreated` domain event pre-save;
                the outbox publishes the corresponding integration event after
                commit so other modules can react.

                **Status codes:**
                - `201 Created` — brand created; `Location` header points at
                  `/v1/operators/brands/{id}` and the body is `{ id }`.
                - `400 Bad Request` — validation failure (missing name, bad slug
                  format, unknown jurisdiction).
                - `401 Unauthorized` / `403 Forbidden` — auth failed or caller
                  lacks `operators.brands.write`.
                - `409 Conflict` — slug already in use for this tenant.
                """
            )
            .Produces(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequireAuthorization($"Permissions.{PermissionCodes.Operators.BrandsWrite}");
    }
}
