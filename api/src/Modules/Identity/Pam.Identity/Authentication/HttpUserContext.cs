using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Pam.Shared.Contracts.Identity;
using Pam.Shared.Security;

namespace Pam.Identity.Authentication;

// Reads the current Actor from the authenticated principal. Registered as the
// IUserContext in AddIdentityModule, replacing the default SystemUserContext
// from Pam.Shared so audit columns reflect who actually did the work.
//
//   no HttpContext          → Actor.System    (background jobs, seeders)
//   unauthenticated request → Actor.Anonymous (anonymous endpoints)
//   authenticated bearer    → Operator + sub  (back-office user)
//
// Player auth lands in Phase 4 — when it does, the Actor.Type discriminator
// will branch on the issuing audience / scope set.
public sealed class HttpUserContext(IHttpContextAccessor accessor) : IUserContext
{
    public Actor Current
    {
        get
        {
            var context = accessor.HttpContext;
            if (context is null)
            {
                return Actor.System;
            }

            var user = context.User;
            if (user.Identity?.IsAuthenticated != true)
            {
                return Actor.Anonymous;
            }

            var sub = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
            return string.IsNullOrEmpty(sub) ? Actor.Anonymous : new Actor(ActorType.Operator, sub);
        }
    }

    public string DisplayName => Current.Id;
}
