using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using OpenIddict.Validation.AspNetCore;
using Pam.Identity.Users.Models;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Pam.Identity.OpenIddict.Controllers;

// /connect/userinfo — returns OIDC standard claims based on granted scopes.
// Tokens are validated against the local OpenIddict server (UseLocalServer
// in IdentityModule), so [Authorize] uses the validation scheme.
public sealed class UserinfoController(UserManager<BackOfficeUser> userManager) : Controller
{
    [Authorize(AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)]
    [HttpGet("~/connect/userinfo")]
    [HttpPost("~/connect/userinfo")]
    [Produces("application/json")]
    public async Task<IActionResult> Userinfo()
    {
        var user = await userManager.FindByIdAsync(User.GetClaim(Claims.Subject)!);
        if (user is null)
        {
            return Challenge(
                authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                properties: new AuthenticationProperties(
                    new Dictionary<string, string?>(StringComparer.Ordinal)
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] =
                            Errors.InvalidToken,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
                            "The specified access token is bound to an account that no longer exists.",
                    }
                )
            );
        }

        var claims = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [Claims.Subject] = await userManager.GetUserIdAsync(user),
        };

        if (User.HasScope(Scopes.Email))
        {
            claims[Claims.Email] = await userManager.GetEmailAsync(user);
            claims[Claims.EmailVerified] = await userManager.IsEmailConfirmedAsync(user);
        }

        if (User.HasScope(Scopes.Profile))
        {
            claims[Claims.Name] = await userManager.GetUserNameAsync(user);
            claims[Claims.PreferredUsername] = await userManager.GetUserNameAsync(user);
        }

        if (User.HasScope(Scopes.Roles))
        {
            claims[Claims.Role] = await userManager.GetRolesAsync(user);
        }

        return Ok(claims);
    }
}
