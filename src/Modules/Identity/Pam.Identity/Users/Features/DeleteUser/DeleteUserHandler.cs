using Microsoft.AspNetCore.Identity;
using Pam.Identity.Users.Exceptions;
using Pam.Identity.Users.Models;
using Pam.Shared.Contracts.CQRS;
using Pam.Shared.Exceptions;

namespace Pam.Identity.Users.Features.DeleteUser;

// Soft-delete via LockoutEnd = far future. Hard-delete would violate the
// regulatory retention expectation (the user's actions must remain auditable).
// Lockout is the in-flight signal Identity already uses to deny SignIn —
// piggybacking on it means SignInManager naturally rejects deleted users.
public sealed class DeleteUserHandler(UserManager<BackOfficeUser> userManager)
    : ICommandHandler<DeleteUserCommand>
{
    private static readonly DateTimeOffset SoftDeleteLockout = DateTimeOffset.MaxValue;

    public async Task Handle(DeleteUserCommand command, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(command.UserId.ToString());
        if (user is null)
        {
            throw new NotFoundException(
                UserErrors.NotFound,
                $"No back-office user found with id '{command.UserId}'."
            );
        }

        // SetLockoutEnabledAsync(true) is a no-op if it's already enabled in
        // the seeder defaults; SetLockoutEndDateAsync is what carries the
        // "deleted" signal.
        var enableResult = await userManager.SetLockoutEnabledAsync(user, true);
        if (!enableResult.Succeeded)
        {
            throw new BusinessRuleViolationException(
                UserErrors.DeleteFailed,
                FormatErrors(enableResult)
            );
        }

        var lockoutResult = await userManager.SetLockoutEndDateAsync(user, SoftDeleteLockout);
        if (!lockoutResult.Succeeded)
        {
            throw new BusinessRuleViolationException(
                UserErrors.DeleteFailed,
                FormatErrors(lockoutResult)
            );
        }

        // Rotate the security stamp so any outstanding access/refresh tokens
        // fail validation at the next security-stamp check, kicking the user
        // out within the validation interval.
        await userManager.UpdateSecurityStampAsync(user);
    }

    private static string FormatErrors(IdentityResult result) =>
        string.Join("; ", result.Errors.Select(e => $"{e.Code}: {e.Description}"));
}
