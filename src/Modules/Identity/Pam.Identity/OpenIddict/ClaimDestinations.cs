using System.Security.Claims;
using OpenIddict.Abstractions;
using Pam.Identity.Contracts.Permissions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Pam.Identity.OpenIddict;

// OpenIddict needs an explicit destination per claim — by default nothing is
// emitted into the access or identity token. The standard rule (Velusia
// pattern) is "always to access; to identity if the matching scope was
// granted." PAM-specific authz claims (permission, brand_id) live on the
// access token only; the identity token is for OIDC profile claims.
//
// Critical: the security stamp is yielded to nothing — leaking it into a
// token would let a holder forge cookie sessions.
public static class ClaimDestinations
{
    public static IEnumerable<string> GetDestinations(Claim claim)
    {
        switch (claim.Type)
        {
            case Claims.Name
            or Claims.PreferredUsername:
                yield return Destinations.AccessToken;
                if (claim.Subject!.HasScope(Scopes.Profile))
                {
                    yield return Destinations.IdentityToken;
                }
                yield break;

            case Claims.Email:
                yield return Destinations.AccessToken;
                if (claim.Subject!.HasScope(Scopes.Email))
                {
                    yield return Destinations.IdentityToken;
                }
                yield break;

            case Claims.Role:
                yield return Destinations.AccessToken;
                if (claim.Subject!.HasScope(Scopes.Roles))
                {
                    yield return Destinations.IdentityToken;
                }
                yield break;

            // PAM-specific authz claims — access token only. The SPA reads
            // userinfo for display data; these drive [Authorize(Policy=…)]
            // checks at the API edge.
            case PamClaimTypes.Permission:
            case PamClaimTypes.BrandId:
                yield return Destinations.AccessToken;
                yield break;

            // Identity's security stamp must never leave the cookie. If a
            // holder of an access token could read it, they could mint
            // cookies and bypass the stamp validator.
            case "AspNet.Identity.SecurityStamp":
                yield break;

            default:
                yield return Destinations.AccessToken;
                yield break;
        }
    }
}
