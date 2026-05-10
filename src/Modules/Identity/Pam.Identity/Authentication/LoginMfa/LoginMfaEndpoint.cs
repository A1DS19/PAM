using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Pam.Identity.Authentication.LoginMfa;

// POST /v1/identity/login/mfa  { code, rememberMachine? }
//   204      success — auth cookie upgraded from partial to full
//   401      bad code or no partial cookie
//   423      locked out
public sealed class LoginMfaEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(
                "/v1/identity/login/mfa",
                async (LoginMfaBody body, ISender sender, CancellationToken ct) =>
                {
                    var result = await sender.Send(
                        new LoginMfaCommand(body.Code, body.RememberMachine),
                        ct
                    );

                    if (result.IsLockedOut)
                    {
                        return Results.Problem(
                            title: "Locked out",
                            detail: "Too many failed MFA attempts. Try again later.",
                            statusCode: StatusCodes.Status423Locked
                        );
                    }

                    if (!result.Succeeded)
                    {
                        return Results.Problem(
                            title: "Unauthorized",
                            detail: "The provided authenticator code is invalid.",
                            statusCode: StatusCodes.Status401Unauthorized
                        );
                    }

                    return Results.NoContent();
                }
            )
            .AllowAnonymous()
            .RequireRateLimiting("auth-sensitive")
            .WithTags("Identity")
            .WithName("LoginMfa")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status423Locked)
            .ProducesValidationProblem();
    }
}

public sealed record LoginMfaBody([FromBody] string Code, bool RememberMachine = false);
