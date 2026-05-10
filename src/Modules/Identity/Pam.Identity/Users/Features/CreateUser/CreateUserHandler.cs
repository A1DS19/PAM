using Microsoft.AspNetCore.Identity;
using Pam.Identity.Users.Exceptions;
using Pam.Identity.Users.Models;
using Pam.Shared.Contracts.CQRS;
using Pam.Shared.Exceptions;

namespace Pam.Identity.Users.Features.CreateUser;

// Admin-create flow. Back-office users are never self-service: only callers
// with `identity.users.write` can hit this endpoint. The bootstrap Owner is
// the on-ramp; every subsequent user is created here.
public sealed class CreateUserHandler(UserManager<BackOfficeUser> userManager)
    : ICommandHandler<CreateUserCommand, Guid>
{
    public async Task<Guid> Handle(CreateUserCommand command, CancellationToken cancellationToken)
    {
        var existing = await userManager.FindByEmailAsync(command.Email);
        if (existing is not null)
        {
            throw new AlreadyExistsException(
                UserErrors.EmailTaken,
                $"A back-office user with email '{command.Email}' already exists."
            );
        }

        var user = new BackOfficeUser
        {
            Id = Guid.CreateVersion7(),
            UserName = command.Email,
            Email = command.Email,
            BrandId = command.BrandId,
            EmailConfirmed = false,
        };

        var createResult = await userManager.CreateAsync(user, command.Password);
        if (!createResult.Succeeded)
        {
            throw new BusinessRuleViolationException(
                UserErrors.CreateFailed,
                FormatErrors(createResult)
            );
        }

        if (command.Roles.Count > 0)
        {
            var roleResult = await userManager.AddToRolesAsync(user, command.Roles);
            if (!roleResult.Succeeded)
            {
                // Roll back the user — leaving an orphan user without the roles
                // the admin asked for is worse than failing the whole call.
                await userManager.DeleteAsync(user);
                throw new BusinessRuleViolationException(
                    UserErrors.CreateFailed,
                    FormatErrors(roleResult)
                );
            }
        }

        return user.Id;
    }

    private static string FormatErrors(IdentityResult result) =>
        string.Join("; ", result.Errors.Select(e => $"{e.Code}: {e.Description}"));
}
