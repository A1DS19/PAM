namespace Pam.Players.Players.ValueObjects;

public sealed record DateOfBirth(DateOnly Value)
{
    public int AgeAt(DateTimeOffset asOfUtc)
    {
        var today = DateOnly.FromDateTime(asOfUtc.UtcDateTime);
        var age = today.Year - Value.Year;
        if (today < Value.AddYears(age))
        {
            age--;
        }
        return age;
    }
}
