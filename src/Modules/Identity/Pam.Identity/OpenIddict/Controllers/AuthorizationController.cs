using System.Security.Claims;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using Pam.Identity.Contracts.Permissions;
using Pam.Identity.Permissions;
using Pam.Identity.Users.Models;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Pam.Identity.OpenIddict.Controllers;

// Custom controller required by OpenIddict to drive the interactive flows.
// Adapted from the Velusia sample with two simplifications:
//   - ConsentTypes.Implicit on the back-office SPA — no consent screen, so
//     no Accept/Deny POST actions and no view templates.
//   - The login UI lives in the React SPA, not Razor — the cookie middleware
//     redirects unauthenticated browsers to BackOfficeSpaOptions.LoginUrl.
public sealed class AuthorizationController(
    IOpenIddictApplicationManager applicationManager,
    IOpenIddictAuthorizationManager authorizationManager,
    IOpenIddictScopeManager scopeManager,
    SignInManager<BackOfficeUser> signInManager,
    UserManager<BackOfficeUser> userManager,
    PermissionResolver permissionResolver
) : Controller
{
    [HttpGet("~/connect/authorize")]
    [HttpPost("~/connect/authorize")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Authorize()
    {
        var request =
            HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException(
                "The OpenID Connect request cannot be retrieved."
            );

        // Try the cookie. If absent or stale (prompt=login / max_age expired),
        // bounce the browser through the SPA login page and back to here.
        var result = await HttpContext.AuthenticateAsync();
        if (
            result is not { Succeeded: true }
            || (
                request.HasPromptValue(PromptValues.Login)
                || request.MaxAge is 0
                || (
                    request.MaxAge is not null
                    && result.Properties?.IssuedUtc is not null
                    && TimeProvider.System.GetUtcNow() - result.Properties.IssuedUtc
                        > TimeSpan.FromSeconds(request.MaxAge.Value)
                )
            )
        )
        {
            // Promptless authentication requested but no session — return the
            // OIDC error so the SPA can fall back to interactive login.
            if (request.HasPromptValue(PromptValues.None))
            {
                return Forbid(
                    authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                    properties: new AuthenticationProperties(
                        new Dictionary<string, string?>(StringComparer.Ordinal)
                        {
                            [OpenIddictServerAspNetCoreConstants.Properties.Error] =
                                Errors.LoginRequired,
                            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
                                "The user is not logged in.",
                        }
                    )
                );
            }

            // Build the returnUrl as the original /connect/authorize URL with
            // its parameters preserved. The cookie middleware will redirect
            // the browser to BackOfficeSpaOptions.LoginUrl with this as
            // ?returnUrl=...; the SPA bounces back here once logged in.
            var qs = Request.HasFormContentType
                ? QueryString.Create(
                    Request.Form.SelectMany(kv =>
                        kv.Value.Select(v => KeyValuePair.Create(kv.Key, v))
                    )
                )
                : QueryString.Create(
                    Request.Query.SelectMany(kv =>
                        kv.Value.Select(v => KeyValuePair.Create(kv.Key, v))
                    )
                );

            return Challenge(
                new AuthenticationProperties { RedirectUri = Request.PathBase + Request.Path + qs }
            );
        }

        var user =
            await userManager.GetUserAsync(result.Principal)
            ?? throw new InvalidOperationException("The user details cannot be retrieved.");

        var application =
            await applicationManager.FindByClientIdAsync(request.ClientId!)
            ?? throw new InvalidOperationException(
                "Details concerning the calling client application cannot be found."
            );

        var existing = await authorizationManager
            .FindAsync(
                subject: await userManager.GetUserIdAsync(user),
                client: await applicationManager.GetIdAsync(application),
                status: Statuses.Valid,
                type: AuthorizationTypes.Permanent,
                scopes: request.GetScopes()
            )
            .ToListAsync();

        // Phase-3 SPA is ConsentTypes.Implicit, so we always issue. Explicit /
        // External / Systematic consent paths are kept for the day a third-
        // party (or a more locked-down operator policy) is registered.
        switch (await applicationManager.GetConsentTypeAsync(application))
        {
            case ConsentTypes.External when existing.Count is 0:
                return Forbid(
                    authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                    properties: new AuthenticationProperties(
                        new Dictionary<string, string?>(StringComparer.Ordinal)
                        {
                            [OpenIddictServerAspNetCoreConstants.Properties.Error] =
                                Errors.ConsentRequired,
                            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
                                "The logged in user is not allowed to access this client application.",
                        }
                    )
                );

            case ConsentTypes.Implicit:
            case ConsentTypes.External when existing.Count is not 0:
            case ConsentTypes.Explicit
                when existing.Count is not 0 && !request.HasPromptValue(PromptValues.Consent):
                var identity = await BuildIdentityAsync(user, request);

                var authorization = existing.LastOrDefault();
                authorization ??= await authorizationManager.CreateAsync(
                    identity: identity,
                    subject: await userManager.GetUserIdAsync(user),
                    client: (await applicationManager.GetIdAsync(application))!,
                    type: AuthorizationTypes.Permanent,
                    scopes: identity.GetScopes()
                );

                identity.SetAuthorizationId(await authorizationManager.GetIdAsync(authorization));
                identity.SetDestinations(ClaimDestinations.GetDestinations);

                return SignIn(
                    new ClaimsPrincipal(identity),
                    OpenIddictServerAspNetCoreDefaults.AuthenticationScheme
                );

            // Explicit / Systematic without prior consent + the SPA's not the
            // path here — fall through to a consent_required error. A future
            // back-office "approve external app" flow would render a page.
            case ConsentTypes.Explicit when request.HasPromptValue(PromptValues.None):
            case ConsentTypes.Systematic when request.HasPromptValue(PromptValues.None):
            default:
                return Forbid(
                    authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                    properties: new AuthenticationProperties(
                        new Dictionary<string, string?>(StringComparer.Ordinal)
                        {
                            [OpenIddictServerAspNetCoreConstants.Properties.Error] =
                                Errors.ConsentRequired,
                            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
                                "Interactive user consent is required.",
                        }
                    )
                );
        }
    }

    [HttpPost("~/connect/token")]
    [IgnoreAntiforgeryToken]
    [Produces("application/json")]
    public async Task<IActionResult> Exchange()
    {
        var request =
            HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException(
                "The OpenID Connect request cannot be retrieved."
            );

        if (!request.IsAuthorizationCodeGrantType() && !request.IsRefreshTokenGrantType())
        {
            throw new InvalidOperationException("The specified grant type is not supported.");
        }

        var result = await HttpContext.AuthenticateAsync(
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme
        );

        var subject = result.Principal!.GetClaim(Claims.Subject);
        var user = subject is null ? null : await userManager.FindByIdAsync(subject);
        if (user is null)
        {
            return Forbid(
                authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                properties: new AuthenticationProperties(
                    new Dictionary<string, string?>(StringComparer.Ordinal)
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] =
                            Errors.InvalidGrant,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
                            "The token is no longer valid.",
                    }
                )
            );
        }

        if (!await signInManager.CanSignInAsync(user))
        {
            return Forbid(
                authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                properties: new AuthenticationProperties(
                    new Dictionary<string, string?>(StringComparer.Ordinal)
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] =
                            Errors.InvalidGrant,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
                            "The user is no longer allowed to sign in.",
                    }
                )
            );
        }

        // Refresh the claim set from the live user — roles, permissions, brand
        // may have changed since the original code/refresh token was issued.
        var refreshed = new ClaimsIdentity(
            result.Principal!.Claims,
            authenticationType: TokenValidationParameters.DefaultAuthenticationType,
            nameType: Claims.Name,
            roleType: Claims.Role
        );

        await PopulateClaimsAsync(refreshed, user);
        refreshed.SetDestinations(ClaimDestinations.GetDestinations);

        return SignIn(
            new ClaimsPrincipal(refreshed),
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme
        );
    }

    [HttpGet("~/connect/logout")]
    [HttpPost("~/connect/logout")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Logout()
    {
        // Drop the cookie so subsequent /connect/authorize calls re-prompt.
        await signInManager.SignOutAsync();

        // Tell OpenIddict to redirect to post_logout_redirect_uri.
        return SignOut(
            authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
            properties: new AuthenticationProperties { RedirectUri = "/" }
        );
    }

    private async Task<ClaimsIdentity> BuildIdentityAsync(
        BackOfficeUser user,
        OpenIddictRequest request
    )
    {
        var identity = new ClaimsIdentity(
            authenticationType: TokenValidationParameters.DefaultAuthenticationType,
            nameType: Claims.Name,
            roleType: Claims.Role
        );

        await PopulateClaimsAsync(identity, user);

        identity.SetScopes(request.GetScopes());
        identity.SetResources(
            await scopeManager.ListResourcesAsync(identity.GetScopes()).ToListAsync()
        );

        return identity;
    }

    private async Task PopulateClaimsAsync(ClaimsIdentity identity, BackOfficeUser user)
    {
        identity
            .SetClaim(Claims.Subject, await userManager.GetUserIdAsync(user))
            .SetClaim(Claims.Email, await userManager.GetEmailAsync(user))
            .SetClaim(Claims.Name, await userManager.GetUserNameAsync(user))
            .SetClaim(Claims.PreferredUsername, await userManager.GetUserNameAsync(user));

        var roleNames = await userManager.GetRolesAsync(user);
        identity.SetClaims(Claims.Role, [.. roleNames]);

        var permissions = await permissionResolver.GetPermissionsForRolesAsync(
            roleNames,
            HttpContext.RequestAborted
        );
        if (permissions.Count > 0)
        {
            identity.SetClaims(PamClaimTypes.Permission, [.. permissions]);
        }

        if (user.BrandId is { } brandId)
        {
            identity.SetClaim(PamClaimTypes.BrandId, brandId.ToString());
        }
    }
}
