using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Pam.Identity.Authentication.LoginRecoveryCode;

// POST /v1/identity/login/recovery-code { code }
//   204 success — auth cookie set, code consumed (one-time use)
//   401 invalid code or no partial cookie (ValidationProblemDetails with `errors`)
//   423 locked out
public sealed class LoginRecoveryCodeEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(
                "/v1/identity/login/recovery-code",
                async (LoginRecoveryCodeCommand command, ISender sender, CancellationToken ct) =>
                {
                    await sender.Send(command, ct);
                    return Results.NoContent();
                }
            )
            .AllowAnonymous()
            .RequireRateLimiting("auth-sensitive")
            .WithTags("Identity")
            .WithName("LoginRecoveryCode")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status423Locked)
            .ProducesValidationProblem();
    }
}
