using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Pam.Identity.Authentication.Mfa;

// POST /v1/identity/me/mfa/recovery-codes
//   → { codes: [...] }   plaintext, returned once. Re-issuing invalidates
//     any previously-issued codes.
public sealed class GenerateRecoveryCodesEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(
                "/v1/identity/me/mfa/recovery-codes",
                async (ISender sender, CancellationToken ct) =>
                {
                    var result = await sender.Send(new GenerateRecoveryCodesCommand(), ct);
                    return Results.Ok(result);
                }
            )
            .WithTags("Identity")
            .WithName("GenerateRecoveryCodes")
            .Produces<GenerateRecoveryCodesResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequireAuthorization()
            .RequireRateLimiting("auth-sensitive");
    }
}
