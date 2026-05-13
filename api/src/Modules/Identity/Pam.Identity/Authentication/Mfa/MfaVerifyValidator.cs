using FluentValidation;

namespace Pam.Identity.Authentication.Mfa;

public sealed class MfaVerifyValidator : AbstractValidator<MfaVerifyCommand>
{
    public MfaVerifyValidator()
    {
        // RFC 6238 TOTPs are 6 digits by default; allow whitespace stripping
        // in the handler. Format checks only — actual code validity is
        // verified by UserManager.
        RuleFor(x => x.Code).NotEmpty().MinimumLength(6).MaximumLength(8);
    }
}
