using FluentValidation.Results;
using Microsoft.AspNetCore.Identity;
using Pam.Identity.Authentication.Login;
using Pam.Identity.Users.Models;
using Pam.Shared.Contracts.CQRS;
using Pam.Shared.Exceptions;

namespace Pam.Identity.Authentication.LoginRecoveryCode;

// TwoFactorRecoveryCodeSignInAsync reads the same partial cookie the
// password step left behind, redeems the code (one-time use), and writes
// the full auth cookie if accepted. Non-success outcomes throw to flow
// through CustomExceptionHandler.
public sealed class LoginRecoveryCodeHandler(SignInManager<BackOfficeUser> signInManager)
    : ICommandHandler<LoginRecoveryCodeCommand, LoginResult>
{
    public async Task<LoginResult> Handle(
        LoginRecoveryCodeCommand command,
        CancellationToken cancellationToken
    )
    {
        var stripped = command
            .Code.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);

        var result = await signInManager.TwoFactorRecoveryCodeSignInAsync(stripped);

        if (result.Succeeded)
        {
            return new LoginResult(Succeeded: true, RequiresTwoFactor: false);
        }

        if (result.IsLockedOut)
        {
            throw new AccountLockedException(
                code: "identity.login.recovery_code.locked_out",
                message: "Too many failed recovery-code attempts. Try again later."
            );
        }

        throw new AuthenticationFailedException(
            "The provided recovery code is invalid or already used.",
            new ValidationFailure(
                nameof(LoginRecoveryCodeCommand.Code),
                "The provided recovery code is invalid or already used."
            )
        );
    }
}
