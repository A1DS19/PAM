using Microsoft.AspNetCore.Identity;
using Pam.Identity.Authentication.Login;
using Pam.Identity.Users.Models;
using Pam.Shared.Contracts.CQRS;

namespace Pam.Identity.Authentication.LoginMfa;

// TwoFactorAuthenticatorSignInAsync reads the two-factor partial cookie that
// PasswordSignInAsync set; if there's no partial cookie the call fails with
// !Succeeded and no Identity flags. Reuses the LoginResult shape so the
// endpoint can map to the same status codes as /v1/identity/login.
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

        return new LoginResult(
            Succeeded: result.Succeeded,
            IsLockedOut: result.IsLockedOut,
            RequiresTwoFactor: false
        );
    }
}
