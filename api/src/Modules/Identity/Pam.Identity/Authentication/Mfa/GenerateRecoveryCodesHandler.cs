using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Pam.Identity.Users.Exceptions;
using Pam.Identity.Users.Models;
using Pam.Shared.Contracts.CQRS;
using Pam.Shared.Exceptions;

namespace Pam.Identity.Authentication.Mfa;

public sealed class GenerateRecoveryCodesHandler(
    UserManager<BackOfficeUser> userManager,
    IHttpContextAccessor httpContext
) : ICommandHandler<GenerateRecoveryCodesCommand, GenerateRecoveryCodesResult>
{
    private const int CodeCount = 10;

    public async Task<GenerateRecoveryCodesResult> Handle(
        GenerateRecoveryCodesCommand command,
        CancellationToken cancellationToken
    )
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

        if (!await userManager.GetTwoFactorEnabledAsync(user))
        {
            throw new BusinessRuleViolationException(
                UserErrors.MfaVerifyFailed,
                "Two-factor authentication must be enabled before generating recovery codes."
            );
        }

        var codes = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, CodeCount);
        if (codes is null)
        {
            throw new BusinessRuleViolationException(
                UserErrors.MfaEnrollFailed,
                "Failed to generate recovery codes."
            );
        }

        return new GenerateRecoveryCodesResult([.. codes]);
    }
}
