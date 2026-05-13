using FluentValidation;

namespace Pam.Identity.Authentication.Mfa;

public sealed class MfaDisableValidator : AbstractValidator<MfaDisableCommand>
{
    public MfaDisableValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().MaximumLength(256);
    }
}
