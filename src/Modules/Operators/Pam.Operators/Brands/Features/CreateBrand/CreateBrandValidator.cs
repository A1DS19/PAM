using FluentValidation;

namespace Pam.Operators.Brands.Features.CreateBrand;

public sealed class CreateBrandValidator : AbstractValidator<CreateBrandCommand>
{
    public CreateBrandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().Length(1, 100);

        RuleFor(x => x.Slug)
            .NotEmpty()
            .Matches(SlugPattern)
            .WithMessage(
                "Slug must be 3-64 lowercase alphanumeric chars or hyphens, "
                    + "with no leading, trailing, or consecutive hyphens."
            );

        RuleFor(x => x.Jurisdiction).NotEmpty().Length(2, 8);
    }

    // 3-64 chars total, lowercase alphanumeric + hyphens, no leading/trailing
    // dash, no consecutive dashes.
    private const string SlugPattern = "^(?!.*--)[a-z0-9][a-z0-9-]{1,62}[a-z0-9]$";
}
