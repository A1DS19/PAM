using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pam.Identity.Contracts.Permissions;

namespace Pam.Identity.Users.Features.CreateUser;

// POST /v1/identity/users   { email, password, brandId?, roles[] }
//   201 → { id }, Location header
//   400 → validation
//   409 → email taken
//   422 → Identity password policy / role assignment failure
public sealed class CreateUserEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(
                "/v1/identity/users",
                async (CreateUserCommand command, ISender sender, CancellationToken ct) =>
                {
                    var id = await sender.Send(command, ct);
                    return Results.Created($"/v1/identity/users/{id}", new { id });
                }
            )
            .WithTags("Identity")
            .WithName("CreateUser")
            .WithSummary("Create a back-office user")
            .WithDescription(
                """
                Creates a new back-office user with the given email, password,
                optional `brandId` scoping, and zero-or-more role assignments.
                The user is created with `EmailConfirmed = false` and cannot
                sign in interactively until they confirm via the email link
                (unless `RequireConfirmedEmail` is disabled in Identity options).

                **Auth:** requires `identity.users.write` permission.

                **Idempotency:** not idempotent — re-POST with the same email
                returns `409 Conflict`.

                **Side effects:** auto-fires a confirmation email via the
                Notifications module (best-effort; SMTP failure is logged, not
                fatal).

                **Status codes:**
                - `201 Created` — user created; `Location` header points at
                  `/v1/identity/users/{id}` and the body is `{ id }`.
                - `400 Bad Request` — validation failure (bad email, missing
                  password).
                - `401 Unauthorized` / `403 Forbidden` — auth failed or caller
                  lacks `identity.users.write`.
                - `409 Conflict` — email already in use.
                - `422 Unprocessable Entity` — Identity policy violation
                  (weak password, unknown role).
                """
            )
            .Produces(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequireAuthorization($"Permissions.{PermissionCodes.Identity.UsersWrite}");
    }
}
