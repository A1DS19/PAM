using Microsoft.AspNetCore.Identity;
using Pam.Identity.Users.Exceptions;
using Pam.Identity.Users.Models;
using Pam.Shared.Contracts.CQRS;
using Pam.Shared.Exceptions;

namespace Pam.Identity.Users.Features.ResetMfa;

// Admin escape hatch for "user lost their phone AND their recovery codes."
// Clears the authenticator key, disables 2FA, and rotates the security
// stamp so any sessions held by an attacker who knew the old secret die.
// User can re-enroll TOTP next login via the normal /me/mfa/enroll flow.
public sealed class ResetMfaHandler(UserManager<BackOfficeUser> userManager)
    : ICommandHandler<ResetMfaCommand>
{
    public async Task Handle(ResetMfaCommand command, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(command.UserId.ToString());
        if (user is null)
        {
            throw new NotFoundException(
                UserErrors.NotFound,
                $"No back-office user found with id '{command.UserId}'."
            );
        }

        var disableResult = await userManager.SetTwoFactorEnabledAsync(user, false);
        if (!disableResult.Succeeded)
        {
            throw new BusinessRuleViolationException(
                UserErrors.UpdateFailed,
                string.Join("; ", disableResult.Errors.Select(e => $"{e.Code}: {e.Description}"))
            );
        }

        var resetResult = await userManager.ResetAuthenticatorKeyAsync(user);
        if (!resetResult.Succeeded)
        {
            throw new BusinessRuleViolationException(
                UserErrors.UpdateFailed,
                string.Join("; ", resetResult.Errors.Select(e => $"{e.Code}: {e.Description}"))
            );
        }

        await userManager.UpdateSecurityStampAsync(user);
    }
}
