using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Pam.Identity.Users.Exceptions;
using Pam.Identity.Users.Models;
using Pam.Shared.Contracts.CQRS;
using Pam.Shared.Exceptions;

namespace Pam.Identity.Authentication.ChangePassword;

public sealed class ChangePasswordHandler(
    UserManager<BackOfficeUser> userManager,
    IHttpContextAccessor httpContext
) : ICommandHandler<ChangePasswordCommand>
{
    public async Task Handle(ChangePasswordCommand command, CancellationToken cancellationToken)
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

        var result = await userManager.ChangePasswordAsync(
            user,
            command.CurrentPassword,
            command.NewPassword
        );
        if (!result.Succeeded)
        {
            throw new BusinessRuleViolationException(
                UserErrors.PasswordChangeFailed,
                string.Join("; ", result.Errors.Select(e => $"{e.Code}: {e.Description}"))
            );
        }

        // ChangePasswordAsync already rotates the security stamp; that's
        // enough to invalidate other refresh tokens on the next validation
        // window. Self-initiated change does not force re-login on this
        // device — by design.
    }
}
