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

            // Players-realm tokens carry the player_id custom claim mapped
            // from the Keycloak user attribute. When that claim is present
            // the request is acting as a Player.
            var playerId = user.FindFirst("player_id")?.Value;
            if (!string.IsNullOrEmpty(playerId))
            {
                return new Actor(ActorType.Player, playerId);
            }

            // Future: operators-realm tokens (no player_id claim). For now
            // an authenticated request without a player_id is unreachable —
            // the only registered auth scheme is "players" and that scheme
            // always emits player_id. Until the operators realm lands, fall
            // back to the JWT sub so audit columns still record an id.
            var sub = user.FindFirst("sub")?.Value;
            return string.IsNullOrEmpty(sub) ? Actor.Anonymous : new Actor(ActorType.Operator, sub);
        }
    }

    public string DisplayName =>
        http.HttpContext?.User.FindFirst("preferred_username")?.Value
        ?? http.HttpContext?.User.FindFirst("name")?.Value
        ?? Current.Id;
}
