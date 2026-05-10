using FluentValidation;
using Pam.Identity.Contracts.Roles;

namespace Pam.Identity.Users.Features.CreateUser;

// Format + structural checks only. Password complexity is enforced by ASP.NET
// Core Identity at create time (RequiredLength, RequireDigit, …) — duplicating
// it here would drift. Role names are checked against the seeded allowlist
// so a typo returns 400 instead of a confusing AddToRoleAsync failure.
public sealed class CreateUserValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(254);

        // Identity's RequiredLength is 12; bail early here to give a clean
        // validation error instead of bubbling Identity's "PasswordTooShort"
        // through the business-rule path. The full policy is still enforced
        // by UserManager.CreateAsync.
        RuleFor(x => x.Password).NotEmpty().MinimumLength(12).MaximumLength(256);

        RuleFor(x => x.Roles).NotNull();
        RuleForEach(x => x.Roles)
            .NotEmpty()
            .Must(role => RoleNames.All.Contains(role, StringComparer.Ordinal))
            .WithMessage($"Role must be one of: {string.Join(", ", RoleNames.All)}.");
    }
}
