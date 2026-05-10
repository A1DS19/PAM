using FluentValidation;
using Pam.Identity.Contracts.Roles;

namespace Pam.Identity.Users.Features.ListUsers;

public sealed class ListUsersValidator : AbstractValidator<ListUsersQuery>
{
    public ListUsersValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
        When(
            x => x.Role is not null,
            () =>
                RuleFor(x => x.Role!)
                    .Must(role => RoleNames.All.Contains(role, StringComparer.Ordinal))
                    .WithMessage($"Role must be one of: {string.Join(", ", RoleNames.All)}.")
        );
    }
}
