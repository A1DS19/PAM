using FluentValidation;
using Pam.Players.Players.Exceptions;
using Pam.Players.Players.Models;
using Pam.Shared.Time;

namespace Pam.Players.Players.Features.Register;

public sealed class RegisterPlayerValidator : AbstractValidator<RegisterPlayer>
{
    public RegisterPlayerValidator(IClock clock)
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .WithErrorCode(PlayerErrors.EmailRequired)
            .EmailAddress()
            .WithErrorCode(PlayerErrors.EmailInvalid)
            .MaximumLength(254);

        RuleFor(x => x.Password)
            .NotEmpty()
            .WithErrorCode(PlayerErrors.PasswordWeak)
            .MinimumLength(12)
            .WithErrorCode(PlayerErrors.PasswordWeak)
            .WithMessage("Password must be at least 12 characters.")
            .Matches(@"\d")
            .WithErrorCode(PlayerErrors.PasswordWeak)
            .WithMessage("Password must contain at least one digit.")
            .Matches(@"[A-Z]")
            .WithErrorCode(PlayerErrors.PasswordWeak)
            .WithMessage("Password must contain at least one uppercase letter.")
            .Matches(@"[a-z]")
            .WithErrorCode(PlayerErrors.PasswordWeak)
            .WithMessage("Password must contain at least one lowercase letter.")
            .Matches(@"[^A-Za-z0-9]")
            .WithErrorCode(PlayerErrors.PasswordWeak)
            .WithMessage("Password must contain at least one special character.");

        RuleFor(x => x.FirstName)
            .NotEmpty()
            .WithErrorCode(PlayerErrors.FirstNameRequired)
            .MaximumLength(80);

        RuleFor(x => x.LastName)
            .NotEmpty()
            .WithErrorCode(PlayerErrors.LastNameRequired)
            .MaximumLength(80);

        RuleFor(x => x.DateOfBirth)
            .Must(dob => dob < DateOnly.FromDateTime(clock.UtcNow.UtcDateTime))
            .WithErrorCode(PlayerErrors.DateOfBirthInvalid)
            .WithMessage("Date of birth must be in the past.")
            .Must(dob => CalculateAge(dob, clock.UtcNow) >= Player.MinimumAge)
            .WithErrorCode(PlayerErrors.AgeBelowMinimum)
            .WithMessage($"Player must be at least {Player.MinimumAge} years old.");

        RuleFor(x => x.CountryCode)
            .NotEmpty()
            .WithErrorCode(PlayerErrors.JurisdictionRequired)
            .Length(2)
            .Matches("^[A-Z]{2}$")
            .WithErrorCode(PlayerErrors.JurisdictionRequired)
            .WithMessage("CountryCode must be ISO 3166-1 alpha-2 (two uppercase letters).");
    }

    private static int CalculateAge(DateOnly dob, DateTimeOffset asOfUtc)
    {
        var today = DateOnly.FromDateTime(asOfUtc.UtcDateTime);
        var age = today.Year - dob.Year;
        if (today < dob.AddYears(age))
        {
            age--;
        }
        return age;
    }
}
