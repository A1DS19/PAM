using Microsoft.AspNetCore.Identity;
using Pam.Identity.Users.Exceptions;
using Pam.Identity.Users.Models;
using Pam.Shared.Contracts.CQRS;
using Pam.Shared.Exceptions;

namespace Pam.Identity.Users.Features.UnlockUser;

// Clears lockout that resulted from too many failed logins. Does NOT undo a
// soft-delete (LockoutEnd = MaxValue) — clearing the lockout end on a
// soft-deleted user would resurrect them silently, which is the wrong default.
// A separate "restore-user" endpoint can land if we ever need it.
public sealed class UnlockUserHandler(UserManager<BackOfficeUser> userManager)
    : ICommandHandler<UnlockUserCommand>
{
    public async Task Handle(UnlockUserCommand command, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(command.UserId.ToString());
        if (user is null)
        {
            throw new NotFoundException(
                UserErrors.NotFound,
                $"No back-office user found with id '{command.UserId}'."
            );
        }

        if (user.LockoutEnd == DateTimeOffset.MaxValue)
        {
            throw new BusinessRuleViolationException(
                UserErrors.UpdateFailed,
                "Cannot unlock a soft-deleted user; restore them first."
            );
        }

        var resetResult = await userManager.ResetAccessFailedCountAsync(user);
        if (!resetResult.Succeeded)
        {
            throw new BusinessRuleViolationException(
                UserErrors.UpdateFailed,
                string.Join("; ", resetResult.Errors.Select(e => $"{e.Code}: {e.Description}"))
            );
        }

        var lockoutResult = await userManager.SetLockoutEndDateAsync(user, lockoutEnd: null);
        if (!lockoutResult.Succeeded)
        {
            throw new BusinessRuleViolationException(
                UserErrors.UpdateFailed,
                string.Join("; ", lockoutResult.Errors.Select(e => $"{e.Code}: {e.Description}"))
            );
        }
    }
}
