using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Pam.Identity.Authentication.ForgotPassword;

// POST /v1/identity/forgot-password { email }
//   → 204 always (anti-enumeration). Email is sent only when the user
//     exists and has a confirmed address.
public sealed class ForgotPasswordEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(
                "/v1/identity/forgot-password",
                async (ForgotPasswordCommand command, ISender sender, CancellationToken ct) =>
                {
                    await sender.Send(command, ct);
                    return Results.NoContent();
                }
            )
            .AllowAnonymous()
            .RequireRateLimiting("auth-sensitive")
            .WithTags("Identity")
            .WithName("ForgotPassword")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem();
    }
}
