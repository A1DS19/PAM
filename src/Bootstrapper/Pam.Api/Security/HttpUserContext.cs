using Microsoft.AspNetCore.Http;
using Pam.Shared.Contracts.Identity;
using Pam.Shared.Security;

namespace Pam.Api.Security;

public sealed class HttpUserContext(IHttpContextAccessor http) : IUserContext
{
    public Actor Current
    {
        get
        {
            var user = http.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true)
            {
                return Actor.Anonymous;
            }

            // The IDP `sub` is the canonical identity-provider user id. PAM's
            // own PlayerId is held server-side on the Player aggregate
            // (`identity_provider_id` column) and resolved by lookup when
            // needed; we don't claim-stuff it into the JWT.
            var sub = user.FindFirst("sub")?.Value;
            if (string.IsNullOrEmpty(sub))
            {
                return Actor.Anonymous;
            }

            // Until the operators audience lands, any authenticated request
            // through the "players" scheme is acting as a Player.
            return new Actor(ActorType.Player, sub);
        }
    }

    public string DisplayName =>
        http.HttpContext?.User.FindFirst("preferred_username")?.Value
        ?? http.HttpContext?.User.FindFirst("name")?.Value
        ?? Current.Id;
}
