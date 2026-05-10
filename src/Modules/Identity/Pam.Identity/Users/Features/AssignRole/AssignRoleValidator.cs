using FluentValidation;
using Pam.Identity.Contracts.Roles;

namespace Pam.Identity.Users.Features.AssignRole;

public sealed class AssignRoleValidator : AbstractValidator<AssignRoleCommand>
{
    public AssignRoleValidator()
    {
        RuleFor(x => x.Role)
            .NotEmpty()
            .Must(role => RoleNames.All.Contains(role, StringComparer.Ordinal))
            .WithMessage($"Role must be one of: {string.Join(", ", RoleNames.All)}.");
    }
}
