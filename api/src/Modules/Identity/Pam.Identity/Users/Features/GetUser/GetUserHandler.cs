using Microsoft.AspNetCore.Identity;
using Pam.Identity.Contracts.Users.Dtos;
using Pam.Identity.Users.Exceptions;
using Pam.Identity.Users.Models;
using Pam.Shared.Contracts.CQRS;
using Pam.Shared.Exceptions;

namespace Pam.Identity.Users.Features.GetUser;

public sealed class GetUserHandler(UserManager<BackOfficeUser> userManager)
    : IQueryHandler<GetUserQuery, BackOfficeUserDto>
{
    public async Task<BackOfficeUserDto> Handle(GetUserQuery query, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(query.UserId.ToString());
        if (user is null)
        {
            throw new NotFoundException(
                UserErrors.NotFound,
                $"No back-office user found with id '{query.UserId}'."
            );
        }

        var roles = await userManager.GetRolesAsync(user);
        return new BackOfficeUserDto(
            Id: user.Id,
            Email: user.Email ?? string.Empty,
            BrandId: user.BrandId,
            EmailConfirmed: user.EmailConfirmed,
            TwoFactorEnabled: user.TwoFactorEnabled,
            LockoutEnabled: user.LockoutEnabled,
            LockoutEnd: user.LockoutEnd,
            Roles: [.. roles],
            CreatedAt: user.CreatedAt,
            LastModifiedAt: user.LastModifiedAt
        );
    }
}
