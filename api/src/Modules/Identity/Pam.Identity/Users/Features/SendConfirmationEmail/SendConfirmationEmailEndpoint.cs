using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Pam.Identity.Contracts.Permissions;

namespace Pam.Identity.Users.Features.SendConfirmationEmail;

public sealed class SendConfirmationEmailEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(
                "/v1/identity/users/{id:guid}/send-confirmation-email",
                async (Guid id, ISender sender, CancellationToken ct) =>
                {
                    await sender.Send(new SendConfirmationEmailCommand(id), ct);
                    return Results.NoContent();
                }
            )
            .WithTags("Identity")
            .WithName("SendConfirmationEmail")
            .WithSummary("Re-send a user's email confirmation link")
            .WithDescription(
                """
                Generates a fresh email-confirmation token for the target user
                and dispatches the confirmation email via the Notifications
                module. Used when the original `CreateUser`-time email was
                lost or expired.

                **Auth:** requires `identity.users.write` permission.

                **Idempotency:** safe to retry — each call generates a new
                token and invalidates any prior unused token for the same
                user.

                **Side effects:** sends an email via Notifications (best
                effort; SMTP failure is logged but does not fail the request).

                **Status codes:**
                - `204 No Content` — request accepted; email dispatched.
                - `401 Unauthorized` / `403 Forbidden` — auth failed or caller
                  lacks `identity.users.write`.
                - `404 Not Found` — no user with that id.
                """
            )
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization($"Permissions.{PermissionCodes.Identity.UsersWrite}");
    }
}
