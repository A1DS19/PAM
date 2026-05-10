using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Pam.Identity.Authentication.Mfa;

// POST /v1/identity/me/mfa/enroll → { sharedKey, authenticatorUri }
public sealed class MfaEnrollEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(
                "/v1/identity/me/mfa/enroll",
                async (ISender sender, CancellationToken ct) =>
                {
                    var result = await sender.Send(new MfaEnrollCommand(), ct);
                    return Results.Ok(result);
                }
            )
            .WithTags("Identity")
            .WithName("MfaEnroll")
            .Produces<MfaEnrollResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequireAuthorization();
    }
}
