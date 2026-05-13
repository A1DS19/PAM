using FluentValidation;

namespace Pam.Identity.Users.Features.UpdateUser;

public sealed class UpdateUserValidator : AbstractValidator<UpdateUserCommand>
{
    public UpdateUserValidator()
    {
        When(x => x.Email is not null, () =>
            RuleFor(x => x.Email!).EmailAddress().MaximumLength(254));
    }
}
