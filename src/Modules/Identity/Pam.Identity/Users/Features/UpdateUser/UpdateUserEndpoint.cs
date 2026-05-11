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
            .WithSummary("Update a back-office user's profile")
            .WithDescription(
                """
                Partially updates a back-office user. All body fields are
                optional; only those present in the request are touched.
                Supports re-assigning brand scope and toggling whether the
                user is subject to lockout policy.

                **Request:** body — any combination of `email`, `brandId`,
                `lockoutEnabled` (all optional).

                **Auth:** requires `identity.users.write` permission.

                **Idempotency:** idempotent — re-PATCH with the same body
                produces the same final state.

                **Side effects:** changing the email rotates the security
                stamp (revokes other sessions) and resets `EmailConfirmed` to
                `false`; the caller is responsible for triggering a new
                confirmation email via `SendConfirmationEmail` if needed.

                **Status codes:**
                - `204 No Content` — user updated.
                - `400 Bad Request` — validation failure.
                - `401 Unauthorized` / `403 Forbidden` — auth failed or caller
                  lacks `identity.users.write`.
                - `404 Not Found` — no user with that id.
                - `409 Conflict` — new email is already in use.
                - `422 Unprocessable Entity` — Identity policy violation.
                """
            )
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
