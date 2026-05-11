using FluentValidation.Results;
using Microsoft.AspNetCore.Identity;
using Pam.Identity.Users.Models;
using Pam.Shared.Contracts.CQRS;
using Pam.Shared.Exceptions;

namespace Pam.Identity.Authentication.Login;

// SignInManager.PasswordSignInAsync sets the auth cookie on the current
// HttpContext as a side effect; non-success outcomes are surfaced as
// thrown exceptions so CustomExceptionHandler renders them as the
// standard ProblemDetails shape every other PAM endpoint uses. The only
// shape the result discriminates is "valid creds, MFA next".
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

        if (result.Succeeded)
        {
            return new LoginResult(Succeeded: true, RequiresTwoFactor: false);
        }

        if (result.RequiresTwoFactor)
        {
            return new LoginResult(Succeeded: false, RequiresTwoFactor: true);
        }

        if (result.IsLockedOut)
        {
            throw new AccountLockedException(
                code: "identity.login.locked_out",
                message: "Too many failed login attempts. Try again later."
            );
        }

        // Uniform "Invalid email or password." — don't disclose which of
        // the two was wrong, so the endpoint isn't a user-enumeration
        // oracle. Per-field entries still let the SPA hang inline errors
        // on both inputs.
        throw new AuthenticationFailedException(
            "Invalid email or password.",
            new ValidationFailure(nameof(LoginCommand.Email), "Invalid email or password."),
            new ValidationFailure(nameof(LoginCommand.Password), "Invalid email or password.")
        );
    }
}
