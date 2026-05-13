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
            .WithSummary("Begin MFA enrollment for the current user")
            .WithDescription(
                """
                Starts TOTP MFA enrollment for the signed-in user. Generates a
                fresh shared secret and returns it along with an
                `otpauth://` URI suitable for rendering as a QR code in the
                authenticator app. The user must then POST a TOTP code to
                `/v1/identity/me/mfa/verify` to finish enrollment — MFA is not
                actually enabled on the account until that step succeeds.

                **Auth:** any authenticated back-office user.

                **Idempotency:** safe to call multiple times — each call
                generates a new shared key and invalidates the previous one.
                Useful if the user lost the QR code before verifying.

                **Side effects:** writes the new shared key into the Identity
                store. Does NOT enable two-factor on the account.

                **Status codes:**
                - `200 OK` — `{ sharedKey, authenticatorUri }` returned.
                - `401 Unauthorized` — caller is not signed in.
                - `422 Unprocessable Entity` — Identity reported a failure
                  generating the key.
                """
            )
            .Produces<MfaEnrollResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequireAuthorization();
    }
}
