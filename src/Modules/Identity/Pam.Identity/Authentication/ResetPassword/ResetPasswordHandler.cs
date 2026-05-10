using Microsoft.AspNetCore.Identity;
using Pam.Identity.Users.Exceptions;
using Pam.Identity.Users.Models;
using Pam.Shared.Contracts.CQRS;
using Pam.Shared.Exceptions;

namespace Pam.Identity.Authentication.ResetPassword;

// Same anti-enumeration shape as forgot-password: if the email doesn't
// resolve, behave as if the token were invalid (422). Don't leak whether
// the address exists by branching on success/404.
public sealed class ResetPasswordHandler(UserManager<BackOfficeUser> userManager)
    : ICommandHandler<ResetPasswordCommand>
{
    public async Task Handle(ResetPasswordCommand command, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(command.Email);
        if (user is null)
        {
            throw new BusinessRuleViolationException(
                UserErrors.PasswordChangeFailed,
                "Invalid email or token."
            );
        }

        var result = await userManager.ResetPasswordAsync(
            user,
            command.Token,
            command.NewPassword
        );
        if (!result.Succeeded)
        {
            throw new BusinessRuleViolationException(
                UserErrors.PasswordChangeFailed,
                string.Join("; ", result.Errors.Select(e => $"{e.Code}: {e.Description}"))
            );
        }

        // ResetPasswordAsync rotates the security stamp; existing sessions
        // on other devices die at the next stamp-validation tick.
    }
}
