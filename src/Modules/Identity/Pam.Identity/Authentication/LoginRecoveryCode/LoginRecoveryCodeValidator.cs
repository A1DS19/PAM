using FluentValidation;

namespace Pam.Identity.Authentication.LoginRecoveryCode;

public sealed class LoginRecoveryCodeValidator : AbstractValidator<LoginRecoveryCodeCommand>
{
    public LoginRecoveryCodeValidator()
    {
        // Identity's default recovery code format is two 5-char groups
        // joined by a dash (e.g. "abcde-fghij"). Allow whitespace/dashes
        // to be stripped in the handler.
        RuleFor(x => x.Code).NotEmpty().MaximumLength(32);
    }
}
