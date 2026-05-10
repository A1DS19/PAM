using Microsoft.AspNetCore.Identity;
using Pam.Identity.Users.Exceptions;
using Pam.Identity.Users.Models;
using Pam.Shared.Contracts.CQRS;
using Pam.Shared.Exceptions;

namespace Pam.Identity.Users.Features.UpdateUser;

public sealed class UpdateUserHandler(UserManager<BackOfficeUser> userManager)
    : ICommandHandler<UpdateUserCommand>
{
    public async Task Handle(UpdateUserCommand command, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(command.UserId.ToString());
        if (user is null)
        {
            throw new NotFoundException(
                UserErrors.NotFound,
                $"No back-office user found with id '{command.UserId}'."
            );
        }

        if (command.Email is { } email
            && !string.Equals(email, user.Email, StringComparison.OrdinalIgnoreCase))
        {
            var collision = await userManager.FindByEmailAsync(email);
            if (collision is not null && collision.Id != user.Id)
            {
                throw new AlreadyExistsException(
                    UserErrors.EmailTaken,
                    $"A back-office user with email '{email}' already exists."
                );
            }

            // SetEmailAsync + SetUserNameAsync keep username == email so login
            // (which is by username under the hood) keeps working. Email
            // confirmation is reset; the user has to re-confirm after change.
            var emailResult = await userManager.SetEmailAsync(user, email);
            if (!emailResult.Succeeded)
            {
                throw new BusinessRuleViolationException(
                    UserErrors.UpdateFailed,
                    FormatErrors(emailResult)
                );
            }
            var userNameResult = await userManager.SetUserNameAsync(user, email);
            if (!userNameResult.Succeeded)
            {
                throw new BusinessRuleViolationException(
                    UserErrors.UpdateFailed,
                    FormatErrors(userNameResult)
                );
            }
        }

        if (command.BrandId is { } brandId && user.BrandId != brandId)
        {
            user.BrandId = brandId;
        }

        if (command.LockoutEnabled is { } enabled && user.LockoutEnabled != enabled)
        {
            var lockoutResult = await userManager.SetLockoutEnabledAsync(user, enabled);
            if (!lockoutResult.Succeeded)
            {
                throw new BusinessRuleViolationException(
                    UserErrors.UpdateFailed,
                    FormatErrors(lockoutResult)
                );
            }
        }

        // Persist BrandId change (the other two went through UserManager which
        // calls UpdateAsync internally). Either way, security stamp rotation
        // is the right hammer for "operator-visible state changed" — it
        // forces re-validation of the user's tokens within the security-stamp
        // validation interval.
        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            throw new BusinessRuleViolationException(
                UserErrors.UpdateFailed,
                FormatErrors(updateResult)
            );
        }
    }

    private static string FormatErrors(IdentityResult result) =>
        string.Join("; ", result.Errors.Select(e => $"{e.Code}: {e.Description}"));
}
