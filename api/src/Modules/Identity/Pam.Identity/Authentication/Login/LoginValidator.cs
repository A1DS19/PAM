using FluentValidation;

namespace Pam.Identity.Authentication.Login;

// Format-only checks — DO NOT enforce password policy here. The user record
// may have been created when a different password policy was in effect; the
// login should still work. Policy is enforced at user creation only.
public sealed class LoginValidator : AbstractValidator<LoginCommand>
{
    public LoginValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(254);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(256);
    }
}
