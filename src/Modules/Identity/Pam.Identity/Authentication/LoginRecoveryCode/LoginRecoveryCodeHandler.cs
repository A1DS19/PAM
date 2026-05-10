using Microsoft.AspNetCore.Identity;
using Pam.Identity.Authentication.Login;
using Pam.Identity.Users.Models;
using Pam.Shared.Contracts.CQRS;

namespace Pam.Identity.Authentication.LoginRecoveryCode;

// TwoFactorRecoveryCodeSignInAsync reads the same partial cookie that
// password-step left behind, redeems the code (one-time use), and writes
// the full auth cookie if accepted.
public sealed class LoginRecoveryCodeHandler(SignInManager<BackOfficeUser> signInManager)
    : ICommandHandler<LoginRecoveryCodeCommand, LoginResult>
{
    public async Task<LoginResult> Handle(
        LoginRecoveryCodeCommand command,
        CancellationToken cancellationToken
    )
    {
        var stripped = command.Code.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);

        var result = await signInManager.TwoFactorRecoveryCodeSignInAsync(stripped);

        return new LoginResult(
            Succeeded: result.Succeeded,
            IsLockedOut: result.IsLockedOut,
            RequiresTwoFactor: false
        );
    }
}
