namespace Pam.Players.Players.Exceptions;

public static class PlayerErrors
{
    public const string EmailRequired = "pam.player.email_required";
    public const string EmailInvalid = "pam.player.email_invalid";
    public const string EmailAlreadyRegistered = "pam.player.email_already_registered";
    public const string PasswordWeak = "pam.player.password_weak";
    public const string DateOfBirthInvalid = "pam.player.dob_invalid";
    public const string AgeBelowMinimum = "pam.player.age_below_minimum";
    public const string JurisdictionRequired = "pam.player.jurisdiction_required";
    public const string JurisdictionNotAllowed = "pam.player.jurisdiction_not_allowed";
    public const string FirstNameRequired = "pam.player.first_name_required";
    public const string LastNameRequired = "pam.player.last_name_required";
}
