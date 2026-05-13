using FluentValidation;

namespace Pam.Identity.Authentication.LoginMfa;

public sealed class LoginMfaValidator : AbstractValidator<LoginMfaCommand>
{
    public LoginMfaValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MinimumLength(6).MaximumLength(8);
    }
}
