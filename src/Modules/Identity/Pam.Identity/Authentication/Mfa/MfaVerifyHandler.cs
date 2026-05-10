using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Pam.Identity.Users.Exceptions;
using Pam.Identity.Users.Models;
using Pam.Shared.Contracts.CQRS;
using Pam.Shared.Exceptions;

namespace Pam.Identity.Authentication.Mfa;

public sealed class MfaVerifyHandler(
    UserManager<BackOfficeUser> userManager,
    IHttpContextAccessor httpContext
) : ICommandHandler<MfaVerifyCommand>
{
    public async Task Handle(MfaVerifyCommand command, CancellationToken cancellationToken)
    {
        var principal =
            httpContext.HttpContext?.User
            ?? throw new UnauthorizedAccessException("No HTTP context.");
        var sub =
            principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(sub))
        {
            throw new UnauthorizedAccessException("Caller has no sub claim.");
        }

        var user =
            await userManager.FindByIdAsync(sub)
            ?? throw new UnauthorizedAccessException("Authenticated user no longer exists.");

        var stripped = command
            .Code.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);
        var verified = await userManager.VerifyTwoFactorTokenAsync(
            user,
            userManager.Options.Tokens.AuthenticatorTokenProvider,
            stripped
        );
        if (!verified)
        {
            throw new BusinessRuleViolationException(
                UserErrors.MfaVerifyFailed,
                "The provided authenticator code is invalid or expired."
            );
        }

        var enableResult = await userManager.SetTwoFactorEnabledAsync(user, true);
        if (!enableResult.Succeeded)
        {
            throw new BusinessRuleViolationException(
                UserErrors.MfaVerifyFailed,
                string.Join("; ", enableResult.Errors.Select(e => $"{e.Code}: {e.Description}"))
            );
        }
    }
}
