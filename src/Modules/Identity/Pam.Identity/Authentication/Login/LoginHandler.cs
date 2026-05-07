using Microsoft.AspNetCore.Identity;
using Pam.Identity.Users.Models;
using Pam.Shared.Contracts.CQRS;

namespace Pam.Identity.Authentication.Login;

// SignInManager.PasswordSignInAsync sets the auth cookie on the current
// HttpContext as a side effect — the endpoint just needs to map the result
// to an HTTP status code.
//
// `lockoutOnFailure: true` ensures the lockout policy from IdentityModule
// (5 attempts / 15 min) is enforced. The `auth-sensitive` rate limiter on
// the endpoint is the outer brute-force gate.
public sealed class LoginHandler(SignInManager<BackOfficeUser> signInManager)
    : ICommandHandler<LoginCommand, LoginResult>
{
    public async Task<LoginResult> Handle(LoginCommand cmd, CancellationToken cancellationToken)
    {
        var result = await signInManager.PasswordSignInAsync(
            userName: cmd.Email,
            password: cmd.Password,
            isPersistent: cmd.RememberMe,
            lockoutOnFailure: true
        );

        return new LoginResult(
            Succeeded: result.Succeeded,
            IsLockedOut: result.IsLockedOut,
            RequiresTwoFactor: result.RequiresTwoFactor
        );
    }
}
