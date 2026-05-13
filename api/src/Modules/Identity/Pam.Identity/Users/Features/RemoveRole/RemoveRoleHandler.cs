using Microsoft.AspNetCore.Identity;
using Pam.Identity.Users.Exceptions;
using Pam.Identity.Users.Models;
using Pam.Shared.Contracts.CQRS;
using Pam.Shared.Exceptions;

namespace Pam.Identity.Users.Features.RemoveRole;

public sealed class RemoveRoleHandler(UserManager<BackOfficeUser> userManager)
    : ICommandHandler<RemoveRoleCommand>
{
    public async Task Handle(RemoveRoleCommand command, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(command.UserId.ToString());
        if (user is null)
        {
            throw new NotFoundException(
                UserErrors.NotFound,
                $"No back-office user found with id '{command.UserId}'."
            );
        }

        if (!await userManager.IsInRoleAsync(user, command.Role))
        {
            return; // idempotent
        }

        var result = await userManager.RemoveFromRoleAsync(user, command.Role);
        if (!result.Succeeded)
        {
            throw new BusinessRuleViolationException(
                UserErrors.UpdateFailed,
                string.Join("; ", result.Errors.Select(e => $"{e.Code}: {e.Description}"))
            );
        }

        await userManager.UpdateSecurityStampAsync(user);
    }
}
