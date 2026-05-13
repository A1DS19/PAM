using Microsoft.AspNetCore.Identity;
using Pam.Identity.Users.Exceptions;
using Pam.Identity.Users.Models;
using Pam.Shared.Contracts.CQRS;
using Pam.Shared.Exceptions;

namespace Pam.Identity.Authentication.ConfirmEmail;

// Anonymous endpoint — the user is clicking a link from email and may not
// have a session yet. The (email, token) pair is the credential. Same
// anti-enumeration posture: missing user → "invalid token" message.
public sealed class ConfirmEmailHandler(UserManager<BackOfficeUser> userManager)
    : ICommandHandler<ConfirmEmailCommand>
{
    public async Task Handle(ConfirmEmailCommand command, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(command.Email);
        if (user is null)
        {
            throw new BusinessRuleViolationException(
                UserErrors.UpdateFailed,
                "Invalid email or token."
            );
        }

        var result = await userManager.ConfirmEmailAsync(user, command.Token);
        if (!result.Succeeded)
        {
            throw new BusinessRuleViolationException(
                UserErrors.UpdateFailed,
                string.Join("; ", result.Errors.Select(e => $"{e.Code}: {e.Description}"))
            );
        }
    }
}
