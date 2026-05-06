using FluentAssertions;
using Pam.Players.Players.ValueObjects;
using Xunit;

namespace Pam.Players.UnitTests.Players.ValueObjects;

public class DateOfBirthTests
{
    [Fact]
    public void AgeAt_returns_full_years_passed()
    {
        var dob = new DateOfBirth(new DateOnly(1990, 6, 15));
        var asOf = new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero);

        dob.AgeAt(asOf).Should().Be(36);
    }

    [Fact]
    public void AgeAt_does_not_count_birthday_until_it_arrives()
    {
        var dob = new DateOfBirth(new DateOnly(1990, 6, 15));
        var dayBefore = new DateTimeOffset(2026, 6, 14, 23, 59, 59, TimeSpan.Zero);

        dob.AgeAt(dayBefore).Should().Be(35);
    }

    [Fact]
    public void AgeAt_handles_leap_day_birthday_on_non_leap_year()
    {
        var dob = new DateOfBirth(new DateOnly(2000, 2, 29));

        // DateOnly.AddYears(26) on Feb 29, 2000 yields Feb 28, 2026 in a
        // non-leap year — the algorithm treats Feb 28 as the substitute
        // birthday, matching the most common jurisdictional rule.
        var birthdaySubstitute = new DateTimeOffset(2026, 2, 28, 0, 0, 0, TimeSpan.Zero);
        dob.AgeAt(birthdaySubstitute).Should().Be(26);

        // The day before the substitute, age has not advanced.
        var dayBefore = new DateTimeOffset(2026, 2, 27, 0, 0, 0, TimeSpan.Zero);
        dob.AgeAt(dayBefore).Should().Be(25);
    }
}
