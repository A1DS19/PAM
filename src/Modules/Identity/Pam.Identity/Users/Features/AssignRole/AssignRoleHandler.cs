using Microsoft.AspNetCore.Identity;
using Pam.Identity.Users.Exceptions;
using Pam.Identity.Users.Models;
using Pam.Shared.Contracts.CQRS;
using Pam.Shared.Exceptions;

namespace Pam.Identity.Users.Features.AssignRole;

public sealed class AssignRoleHandler(
    UserManager<BackOfficeUser> userManager,
    RoleManager<IdentityRole<Guid>> roleManager
) : ICommandHandler<AssignRoleCommand>
{
    public async Task Handle(AssignRoleCommand command, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(command.UserId.ToString());
        if (user is null)
        {
            throw new NotFoundException(
                UserErrors.NotFound,
                $"No back-office user found with id '{command.UserId}'."
            );
        }

        if (!await roleManager.RoleExistsAsync(command.Role))
        {
            throw new NotFoundException(
                UserErrors.RoleNotFound,
                $"Role '{command.Role}' does not exist."
            );
        }

        if (await userManager.IsInRoleAsync(user, command.Role))
        {
            return; // idempotent
        }

        var result = await userManager.AddToRoleAsync(user, command.Role);
        if (!result.Succeeded)
        {
            throw new BusinessRuleViolationException(
                UserErrors.UpdateFailed,
                string.Join("; ", result.Errors.Select(e => $"{e.Code}: {e.Description}"))
            );
        }

        // Permission set changes — rotate the security stamp so tokens
        // re-issue with the updated claim set on next refresh.
        await userManager.UpdateSecurityStampAsync(user);
    }
}
