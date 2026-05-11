using FluentValidation.Results;
using Microsoft.AspNetCore.Identity;
using Pam.Identity.Authentication.Login;
using Pam.Identity.Users.Models;
using Pam.Shared.Contracts.CQRS;
using Pam.Shared.Exceptions;

namespace Pam.Identity.Authentication.LoginMfa;

// TwoFactorAuthenticatorSignInAsync reads the two-factor partial cookie
// that PasswordSignInAsync set; non-success outcomes throw so they flow
// through CustomExceptionHandler like the password step.
public sealed class LoginMfaHandler(SignInManager<BackOfficeUser> signInManager)
    : ICommandHandler<LoginMfaCommand, LoginResult>
{
    public async Task<LoginResult> Handle(
        LoginMfaCommand command,
        CancellationToken cancellationToken
    )
    {
        var stripped = command
            .Code.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);

        var result = await signInManager.TwoFactorAuthenticatorSignInAsync(
            code: stripped,
            isPersistent: false,
            rememberClient: command.RememberMachine
        );

        if (result.Succeeded)
        {
            return new LoginResult(Succeeded: true, RequiresTwoFactor: false);
        }

        if (result.IsLockedOut)
        {
            throw new AccountLockedException(
                code: "identity.login.mfa.locked_out",
                message: "Too many failed MFA attempts. Try again later."
            );
        }

        throw new AuthenticationFailedException(
            "The provided authenticator code is invalid.",
            new ValidationFailure(
                nameof(LoginMfaCommand.Code),
                "The provided authenticator code is invalid."
            )
        );
    }
}
