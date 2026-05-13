using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Pam.Identity.Users.Exceptions;
using Pam.Identity.Users.Models;
using Pam.Shared.Contracts.CQRS;
using Pam.Shared.Exceptions;

namespace Pam.Identity.Authentication.Mfa;

public sealed class MfaDisableHandler(
    UserManager<BackOfficeUser> userManager,
    IHttpContextAccessor httpContext
) : ICommandHandler<MfaDisableCommand>
{
    public async Task Handle(MfaDisableCommand command, CancellationToken cancellationToken)
    {
        var principal = httpContext.HttpContext?.User
            ?? throw new UnauthorizedAccessException("No HTTP context.");
        var sub = principal.FindFirstValue("sub")
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(sub))
        {
            throw new UnauthorizedAccessException("Caller has no sub claim.");
        }

        var user = await userManager.FindByIdAsync(sub)
            ?? throw new UnauthorizedAccessException("Authenticated user no longer exists.");

        if (!await userManager.CheckPasswordAsync(user, command.CurrentPassword))
        {
            throw new BusinessRuleViolationException(
                UserErrors.MfaVerifyFailed,
                "Current password is incorrect."
            );
        }

        // Two steps: clear the authenticator key + flip TwoFactorEnabled.
        // Keeping the key around with 2FA disabled would let a future re-enable
        // skip the verify step and silently re-arm the old secret.
        var disableResult = await userManager.SetTwoFactorEnabledAsync(user, false);
        if (!disableResult.Succeeded)
        {
            throw new BusinessRuleViolationException(
                UserErrors.MfaVerifyFailed,
                string.Join("; ", disableResult.Errors.Select(e => $"{e.Code}: {e.Description}"))
            );
        }

        var resetResult = await userManager.ResetAuthenticatorKeyAsync(user);
        if (!resetResult.Succeeded)
        {
            throw new BusinessRuleViolationException(
                UserErrors.MfaVerifyFailed,
                string.Join("; ", resetResult.Errors.Select(e => $"{e.Code}: {e.Description}"))
            );
        }
    }
}
