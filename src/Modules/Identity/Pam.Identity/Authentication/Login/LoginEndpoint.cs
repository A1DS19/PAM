using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Pam.Identity.Authentication.Login;

// POST /v1/identity/login
//   Request:  { email, password, rememberMe? }
//   200      { mfaRequired: true }   when MFA is enrolled (PR 2 will surface)
//   204      success — auth cookie set, the SPA navigates to ?returnUrl
//   401      invalid credentials
//   423      account locked out (5 failed attempts in 15min window)
//
// The cookie is set by SignInManager on the HttpContext during the handler.
// Anonymous + auth-sensitive rate-limited (5/min/IP).
public sealed class LoginEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(
                "/v1/identity/login",
                async (LoginRequest body, ISender sender, CancellationToken ct) =>
                {
                    var result = await sender.Send(
                        new LoginCommand(body.Email, body.Password, body.RememberMe),
                        ct
                    );

                    if (result.IsLockedOut)
                    {
                        return Results.Problem(
                            title: "Locked out",
                            detail: "Too many failed login attempts. Try again later.",
                            statusCode: StatusCodes.Status423Locked
                        );
                    }

                    if (result.RequiresTwoFactor)
                    {
                        return Results.Ok(new { mfaRequired = true });
                    }

                    if (!result.Succeeded)
                    {
                        return Results.Problem(
                            title: "Unauthorized",
                            detail: "Invalid email or password.",
                            statusCode: StatusCodes.Status401Unauthorized
                        );
                    }

                    return Results.NoContent();
                }
            )
            .AllowAnonymous()
            .RequireRateLimiting("auth-sensitive")
            .WithTags("Identity")
            .WithName("Login")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status423Locked)
            .ProducesValidationProblem();
    }
}

public sealed record LoginRequest(
    [FromBody] string Email,
    string Password,
    bool RememberMe = false
);
